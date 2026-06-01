local M = {}

local function load_actions_lib()
    local ok, mod = pcall(require, "AvePointLevelingActions")
    if ok and type(mod) == "table" then
        return mod
    end

    ok, mod = pcall(require, "scripts.AvePointLevelingActions")
    if ok and type(mod) == "table" then
        return mod
    end

    local chunk, err = loadfile("scripts/AvePointLevelingActions.lua")
    if chunk then
        return chunk()
    end

    error("load AvePointLevelingActions failed: " .. tostring(err))
end

local Actions = load_actions_lib()
local make_boss_kite_task_config = Actions.make_boss_kite_task
local make_world_map_send_task_config = Actions.make_world_map_send_task
local make_dialogue_locator_flow_task_config = Actions.make_dialogue_locator_flow_task
local make_post_dialogue_flow_task_config = Actions.make_post_dialogue_flow_task
local make_fixed_client_click_step = Actions.make_fixed_client_click_step
local make_revive_reentry_config = Actions.make_revive_reentry
local make_npc_dialogue_route_action = Actions.make_npc_dialogue_route_action
local make_route_point_action = Actions.make_route_point_action
local make_maintenance_locator_step = Actions.make_maintenance_locator_step
local make_maintenance_fixed_click_step = Actions.make_maintenance_fixed_click_step
local make_contract_initial_and_second_setup_plan = Actions.make_contract_initial_and_second_setup_plan
local make_contract_second_setup_plan = Actions.make_contract_second_setup_plan

local TRIAL_OF_SUN_THREE_TRIALS_PACKAGE_KEY = "trial_of_sun_three_trials"
local TRIAL_OF_SUN_POWER_SIDE_KEY = "trial_of_sun_power_side_task"
local TRIAL_OF_SUN_CONQUEST_SIDE_KEY = "trial_of_sun_conquest_side_task"
local TRIAL_OF_SUN_BEAUTY_SIDE_KEY = "trial_of_sun_beauty_side_task"
local TRIAL_OF_SUN_MAIN_TASK_PATTERNS = {
    "太阳的试炼",
    "通过三处试炼"
}
local TRIAL_OF_SUN_MAIN_DETAIL_PATTERNS = {
    "通过三处试炼",
    "追击夺走火种",
    "支线 藏宝地"
}

local function read_project_config_number(key, default_value)
    key = tostring(key or "")
    if key == "" then
        return default_value
    end

    local file = io.open("config.json", "rb")
    if not file then
        return default_value
    end

    local text = file:read("*a")
    file:close()
    if type(text) ~= "string" or text == "" then
        return default_value
    end

    local pattern = '"' .. key:gsub("([^%w])", "%%%1") .. '"%s*:%s*(-?%d+)'
    local value = tonumber(text:match(pattern) or "")
    if value == nil then
        return default_value
    end
    return value
end

local function repeat_waypoints(points, count)
    local repeated = {}
    count = math.floor(tonumber(count) or 1)
    if count < 1 then
        count = 1
    end
    for _ = 1, count do
        for _, point in ipairs(points or {}) do
            repeated[#repeated + 1] = {
                x = point.x,
                y = point.y,
                z = point.z
            }
        end
    end
    return repeated
end

local function configured_sun_faction_choice()
    return 1
end

local SUN_FACTION_CHOICE = configured_sun_faction_choice()

M.LEVEL_UP_MAINTENANCE_CONFIG = {
    enabled = true,
    execute_ui = true,
    run_current_level_plan_on_baseline = true,
    catch_up_missing_plans_on_baseline = true,
    suppress_baseline_on_identity_change = true,
    suppress_baseline_on_level_drop = true,
    suppress_baseline_on_low_new_character = true,
    suppress_baseline_when_level_below_persisted = true,
    new_character_baseline_suppress_max_level = 3,
    seed_next_missing_level_when_level_text_missing = false,
    probe_ms = 1200,
    safe_no_monster_ms = 1800,
    skip_safe_window = true,
    monster_guard_enabled = false,
    monster_guard_distance = 1000,
    monster_hard_block_distance = 300,
    nearby_monster_soft_observe_ms = 2000,
    nearby_monster_soft_resource_drop_epsilon = 1,
    nearby_monster_hold_timeout_ms = 7000,
    nearby_monster_defer_retry_ms = 8000,
    min_hp_ratio = 0.72,
    allow_low_hp_maintenance = true,
    defer_revive_during_maintenance = false,
    preserve_executor_on_death = true,
    restart_executor_step_after_revive = true,
    panel_open_death_guard_ms = 2500,
    allow_position_available_without_main_interface = true,
    step_wait_ms = 180,
    adaptive_step_wait_enabled = true,
    adaptive_step_wait_ms = 120,
    adaptive_text_input_wait_ms = 160,
    target_poll_interval_ms = 100,
    target_poll_count = 30,
    retry_wait_cap_ms = 100,
    point_check_retry_count = 30,
    point_check_retry_wait_ms = 100,
    point_decrement_retry_count = 30,
    point_decrement_retry_wait_ms = 100,
    point_decrement_verify_retry_count = 30,
    point_decrement_verify_wait_ms = 100,
    executor_timeout_ms = 90000,
    retry_ms = 5000,
    available_point_probe = {
        min_value = 1,
        min_x = 1180,
        max_y = 130,
        include_plain_number = true
    },
    talent_point_probe = {
        min_value = 1,
        text_name_include = "Talent_C.WidgetTree.TalentTitle.WidgetTree.TalentNum",
        min_x = 1060,
        max_x = 1230,
        min_y = 20,
        max_y = 115,
        include_plain_number = true
    },
    skill_enabled = true,
    skill_extra_enabled = true,
    talent_enabled = true,
    talent_extra_enabled = true,
    contract_enabled = true,
    plan_order = { "skill_extra", "skill", "talent", "talent_extra", "contract" },
    default_skill_enabled = true,
    default_skill_min_level = 1,
    default_skill_catch_up_missing = false,
    default_skill_block_main_flow = false,
    skill_by_level = {
        [3] = {
            key = "level_3_skill_upgrade_sequence",
            label = "3级技能：找图升级技能",
            require_available_points = false,
            close_with_escape = false,
            steps = {
                {
                    key = "open_skill_fast_entrance_menu",
                    label = "技能天赋菜单按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
                    },
                    hint_client_x = 1383.688110,
                    hint_client_y = 52.706509,
                    hint_ratio_x = 0.961562,
                    hint_ratio_y = 0.058563,
                    hint_max_distance = 100,
                    wait_after_ms = 800
                },
                {
                    key = "open_skill_panel",
                    label = "技能按钮",
                    distance_anchor_exact_text = "技能",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn",
                    distance_min = 49.048348,
                    distance_max = 52.082267,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
                    },
                    hint_client_x = 1249.024658,
                    hint_client_y = 155.104156,
                    hint_ratio_x = 0.867981,
                    hint_ratio_y = 0.172338,
                    hint_max_distance = 90,
                    wait_after_ms = 1000
                },
                {
                    key = "click_skill_upgrade_image",
                    label = "技能升级找图按钮",
                    missing_image_means_done = true,
                    cleanup_back_before_finish = true,
                    repeat_image_until_missing = true,
                    repeat_image_until_missing_max_count = 30,
                    repeat_image_until_missing_interval_ms = 180,
                    missing_image_retry_count = 2,
                    missing_image_retry_interval_ms = 300,
                    post_image_done_mouse_away = {
                        rect_left = 1109.00,
                        rect_top = 10.00,
                        rect_right = 1438.00,
                        rect_bottom = 220.00,
                        random_target = true,
                        target_regions = {
                            { left = 840.00, top = 180.00, right = 1040.00, bottom = 320.00 },
                            { left = 720.00, top = 500.00, right = 980.00, bottom = 700.00 },
                            { left = 220.00, top = 160.00, right = 460.00, bottom = 340.00 },
                            { left = 520.00, top = 720.00, right = 800.00, bottom = 840.00 }
                        },
                        move_if_inside_only = true,
                        mouse_mode = "api",
                        hover_ms = 80
                    },
                    image_preset = {
                        template_path = "skill_level_up.bmp",
                        template_threshold = 0.85,
                        click_button = "left",
                        click_delay = 50,
                        click_repeat_count = 1,
                        click_repeat_interval_ms = 120,
                        repeat_until_missing = true,
                        repeat_until_missing_max_count = 30,
                        repeat_until_missing_interval_ms = 180,
                        click_mode = "api",
                        hover_delay_ms = 80,
                        click_center = false,
                        click_offset_x = 20,
                        click_offset_y = 0,
                        allow_gray_fallback = false,
                        capture_set_foreground = true,
                        capture_foreground_delay_ms = 80
                    },
                    wait_after_ms = 800
                },
                {
                    key = "back_from_skill_panel",
                    label = "技能返回按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
                    },
                    hint_client_x = 1369.400024,
                    hint_client_y = 37.000000,
                    hint_ratio_x = 0.950972,
                    hint_ratio_y = 0.041111,
                    hint_max_distance = 30,
                    wait_after_ms = 500
                }
            }
        }
    },
    talent_by_level = {
        [2] = {
            key = "level_2_trickster_god_activate",
            label = "2级天赋：激活欺诈之神",
            require_available_points = "defer",
            close_with_escape = false,
            steps = {
                {
                    key = "open_fast_entrance_menu",
                    label = "技能天赋菜单按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
                    },
                    hint_client_x = 1383.688110,
                    hint_client_y = 52.706509,
                    hint_ratio_x = 0.961562,
                    hint_ratio_y = 0.058563,
                    hint_max_distance = 80,
                    wait_after_ms = 700
                },
                {
                    key = "open_talent_panel",
                    label = "天赋按钮",
                    distance_anchor_exact_text = "天赋",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn",
                    distance_min = 49.048348,
                    distance_max = 52.082267,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
                    },
                    hint_client_x = 1309.033936,
                    hint_client_y = 155.104156,
                    hint_ratio_x = 0.909683,
                    hint_ratio_y = 0.172338,
                    hint_max_distance = 80,
                    wait_after_ms = 1000
                },
                {
                    kind = "check_available_points",
                    key = "check_talent_points",
                    label = "检查天赋点",
                    point_kind = "talent",
                    min_value = 1,
                    retry_count = 3,
                    retry_wait_ms = 500,
                    wait_after_ms = 250
                },
                {
                    key = "select_trickster_god",
                    label = "天赋大类按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.UICareerPointItem_C.WidgetTree.SelectBtn"
                    },
                    hint_client_x = 1049.438477,
                    hint_client_y = 412.786438,
                    hint_ratio_x = 0.729283,
                    hint_ratio_y = 0.458652,
                    hint_max_distance = 80,
                    wait_after_ms = 500
                },
                {
                    key = "activate_trickster_god_category",
                    label = "天赋大类激活按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TipCareerItem_C.WidgetTree.ActiveBtn"
                    },
                    hint_client_x = 1250.157104,
                    hint_client_y = 454.429352,
                    hint_ratio_x = 0.868768,
                    hint_ratio_y = 0.504922,
                    hint_max_distance = 80,
                    wait_after_ms = 500
                },
                {
                    key = "select_first_talent_node",
                    label = "第一个天赋节点按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    hint_client_x = 292.986755,
                    hint_client_y = 560.094788,
                    hint_ratio_x = 0.203604,
                    hint_ratio_y = 0.622328,
                    hint_max_distance = 20,
                    wait_after_ms = 500
                },
                {
                    key = "activate_first_talent_node",
                    label = "第一个天赋节点激活按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    hint_client_x = 503.438232,
                    hint_client_y = 685.801453,
                    hint_ratio_x = 0.349853,
                    hint_ratio_y = 0.762002,
                    hint_max_distance = 45,
                    wait_after_ms = 650
                },
                {
                    key = "back_from_talent_detail",
                    label = "天赋返回按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
                    },
                    hint_client_x = 1373.452393,
                    hint_client_y = 52.000000,
                    hint_ratio_x = 0.954449,
                    hint_ratio_y = 0.057778,
                    hint_max_distance = 45,
                    wait_after_ms = 500
                }
            }
        },
        [3] = {
            key = "level_3_first_talent_node_activate",
            label = "3级天赋：激活第一个天赋节点",
            require_available_points = "defer",
            close_with_escape = false,
            steps = {
                {
                    key = "open_fast_entrance_menu",
                    label = "技能天赋菜单按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
                    },
                    hint_client_x = 1383.688110,
                    hint_client_y = 52.706509,
                    hint_ratio_x = 0.961562,
                    hint_ratio_y = 0.058563,
                    hint_max_distance = 80,
                    wait_after_ms = 700
                },
                {
                    key = "open_talent_panel",
                    label = "天赋按钮",
                    distance_anchor_exact_text = "天赋",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn",
                    distance_min = 49.048348,
                    distance_max = 52.082267,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
                    },
                    hint_client_x = 1309.033936,
                    hint_client_y = 155.104156,
                    hint_ratio_x = 0.909683,
                    hint_ratio_y = 0.172338,
                    hint_max_distance = 80,
                    wait_after_ms = 1000
                },
                {
                    kind = "check_available_points",
                    key = "check_talent_points",
                    label = "检查天赋点",
                    point_kind = "talent",
                    min_value = 1,
                    retry_count = 3,
                    retry_wait_ms = 500,
                    wait_after_ms = 250
                },
                {
                    key = "select_first_talent_node",
                    label = "第一个天赋节点按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    hint_client_x = 292.986755,
                    hint_client_y = 560.094788,
                    hint_ratio_x = 0.203604,
                    hint_ratio_y = 0.622328,
                    hint_max_distance = 20,
                    wait_after_ms = 500
                },
                {
                    key = "activate_first_talent_node",
                    label = "第一个天赋节点激活按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    hint_client_x = 503.438232,
                    hint_client_y = 685.801453,
                    hint_ratio_x = 0.349853,
                    hint_ratio_y = 0.762002,
                    hint_max_distance = 45,
                    wait_after_ms = 650
                },
                {
                    key = "back_from_talent_detail",
                    label = "天赋返回按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
                    },
                    hint_client_x = 1373.452393,
                    hint_client_y = 52.000000,
                    hint_ratio_x = 0.954449,
                    hint_ratio_y = 0.057778,
                    hint_max_distance = 45,
                    wait_after_ms = 500
                }
            }
        },
        [4] = {
            key = "level_4_second_talent_node_activate",
            label = "4级天赋：激活第二个天赋节点",
            require_available_points = "defer",
            close_with_escape = false,
            steps = {
                {
                    key = "open_fast_entrance_menu",
                    label = "技能天赋菜单按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
                    },
                    hint_client_x = 1383.688110,
                    hint_client_y = 52.706509,
                    hint_ratio_x = 0.961562,
                    hint_ratio_y = 0.058563,
                    hint_max_distance = 80,
                    wait_after_ms = 700
                },
                {
                    key = "open_talent_panel",
                    label = "天赋按钮",
                    distance_anchor_exact_text = "天赋",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn",
                    distance_min = 49.048348,
                    distance_max = 52.082267,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
                    },
                    hint_client_x = 1309.033936,
                    hint_client_y = 155.104156,
                    hint_ratio_x = 0.909683,
                    hint_ratio_y = 0.172338,
                    hint_max_distance = 80,
                    wait_after_ms = 1000
                },
                {
                    kind = "check_available_points",
                    key = "check_talent_points",
                    label = "检查天赋点",
                    point_kind = "talent",
                    min_value = 1,
                    retry_count = 3,
                    retry_wait_ms = 500,
                    wait_after_ms = 250
                },
                {
                    key = "select_second_talent_node",
                    label = "第二个天赋节点按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    hint_client_x = 448.916748,
                    hint_client_y = 560.094788,
                    hint_ratio_x = 0.311964,
                    hint_ratio_y = 0.622328,
                    hint_max_distance = 20,
                    wait_after_ms = 500
                },
                {
                    key = "activate_second_talent_node",
                    label = "第二个天赋节点激活按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    hint_client_x = 503.438232,
                    hint_client_y = 685.801453,
                    hint_ratio_x = 0.349853,
                    hint_ratio_y = 0.762002,
                    hint_max_distance = 240,
                    wait_after_ms = 650
                },
                {
                    key = "back_from_talent_detail",
                    label = "天赋返回按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
                    },
                    hint_client_x = 1373.452393,
                    hint_client_y = 52.000000,
                    hint_ratio_x = 0.954449,
                    hint_ratio_y = 0.057778,
                    hint_max_distance = 45,
                    wait_after_ms = 500
                }
            }
        },
        [5] = {
            key = "level_5_second_talent_node_activate",
            label = "5级天赋：激活第二个天赋节点",
            require_available_points = "defer",
            close_with_escape = false,
            steps = {
                {
                    key = "open_fast_entrance_menu",
                    label = "技能天赋菜单按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
                    },
                    hint_client_x = 1383.688110,
                    hint_client_y = 52.706509,
                    hint_ratio_x = 0.961562,
                    hint_ratio_y = 0.058563,
                    hint_max_distance = 80,
                    wait_after_ms = 700
                },
                {
                    key = "open_talent_panel",
                    label = "天赋按钮",
                    distance_anchor_exact_text = "天赋",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn",
                    distance_min = 49.048348,
                    distance_max = 52.082267,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
                    },
                    hint_client_x = 1309.033936,
                    hint_client_y = 155.104156,
                    hint_ratio_x = 0.909683,
                    hint_ratio_y = 0.172338,
                    hint_max_distance = 80,
                    wait_after_ms = 1000
                },
                {
                    kind = "check_available_points",
                    key = "check_talent_points",
                    label = "检查天赋点",
                    point_kind = "talent",
                    min_value = 1,
                    retry_count = 3,
                    retry_wait_ms = 500,
                    wait_after_ms = 250
                },
                {
                    key = "select_second_talent_node",
                    label = "第二个天赋节点按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    hint_client_x = 448.916748,
                    hint_client_y = 560.094788,
                    hint_ratio_x = 0.311964,
                    hint_ratio_y = 0.622328,
                    hint_max_distance = 20,
                    wait_after_ms = 500
                },
                {
                    key = "activate_second_talent_node",
                    label = "第二个天赋节点激活按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    hint_client_x = 503.438232,
                    hint_client_y = 685.801453,
                    hint_ratio_x = 0.349853,
                    hint_ratio_y = 0.762002,
                    hint_max_distance = 240,
                    wait_after_ms = 650
                },
                {
                    key = "back_from_talent_detail",
                    label = "天赋返回按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
                    },
                    hint_client_x = 1373.452393,
                    hint_client_y = 52.000000,
                    hint_ratio_x = 0.954449,
                    hint_ratio_y = 0.057778,
                    hint_max_distance = 45,
                    wait_after_ms = 500
                }
            }
        },
        [6] = {
            key = "level_6_second_talent_node_activate",
            label = "6级天赋：激活第二个天赋节点",
            require_available_points = "defer",
            close_with_escape = false,
            steps = {
                {
                    key = "open_fast_entrance_menu",
                    label = "技能天赋菜单按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
                    },
                    hint_client_x = 1383.688110,
                    hint_client_y = 52.706509,
                    hint_ratio_x = 0.961562,
                    hint_ratio_y = 0.058563,
                    hint_max_distance = 80,
                    wait_after_ms = 700
                },
                {
                    key = "open_talent_panel",
                    label = "天赋按钮",
                    distance_anchor_exact_text = "天赋",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn",
                    distance_min = 49.048348,
                    distance_max = 52.082267,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
                    },
                    hint_client_x = 1309.033936,
                    hint_client_y = 155.104156,
                    hint_ratio_x = 0.909683,
                    hint_ratio_y = 0.172338,
                    hint_max_distance = 80,
                    wait_after_ms = 1000
                },
                {
                    kind = "check_available_points",
                    key = "check_talent_points",
                    label = "检查天赋点",
                    point_kind = "talent",
                    min_value = 1,
                    retry_count = 3,
                    retry_wait_ms = 500,
                    wait_after_ms = 250
                },
                {
                    key = "select_second_talent_node",
                    label = "第二个天赋节点按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    hint_client_x = 448.916748,
                    hint_client_y = 560.094788,
                    hint_ratio_x = 0.311964,
                    hint_ratio_y = 0.622328,
                    hint_max_distance = 20,
                    wait_after_ms = 500
                },
                {
                    key = "activate_second_talent_node",
                    label = "第二个天赋节点激活按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    hint_client_x = 503.438232,
                    hint_client_y = 685.801453,
                    hint_ratio_x = 0.349853,
                    hint_ratio_y = 0.762002,
                    hint_max_distance = 240,
                    wait_after_ms = 650
                },
                {
                    key = "back_from_talent_detail",
                    label = "天赋返回按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
                    },
                    hint_client_x = 1373.452393,
                    hint_client_y = 52.000000,
                    hint_ratio_x = 0.954449,
                    hint_ratio_y = 0.057778,
                    hint_max_distance = 45,
                    wait_after_ms = 500
                }
            }
        },
        [7] = {
            key = "level_7_talent_node_activate",
            label = "7级天赋：激活天赋节点",
            require_available_points = "defer",
            close_with_escape = false,
            steps = {
                {
                    key = "open_fast_entrance_menu",
                    label = "技能天赋菜单按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
                    },
                    hint_client_x = 1383.688110,
                    hint_client_y = 52.706509,
                    hint_ratio_x = 0.961562,
                    hint_ratio_y = 0.058563,
                    hint_max_distance = 80,
                    wait_after_ms = 700
                },
                {
                    key = "open_talent_panel",
                    label = "天赋按钮",
                    distance_anchor_exact_text = "天赋",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn",
                    distance_min = 49.048348,
                    distance_max = 52.082267,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
                    },
                    hint_client_x = 1309.033936,
                    hint_client_y = 155.104156,
                    hint_ratio_x = 0.909683,
                    hint_ratio_y = 0.172338,
                    hint_max_distance = 80,
                    wait_after_ms = 1000
                },
                {
                    kind = "check_available_points",
                    key = "check_talent_points",
                    label = "检查天赋点",
                    point_kind = "talent",
                    min_value = 1,
                    retry_count = 3,
                    retry_wait_ms = 500,
                    wait_after_ms = 250
                },
                {
                    key = "select_level_7_talent_node",
                    label = "天赋节点按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    hint_client_x = 462.916748,
                    hint_client_y = 633.538025,
                    hint_ratio_x = 0.321693,
                    hint_ratio_y = 0.703931,
                    hint_max_distance = 20,
                    wait_after_ms = 1200
                },
                {
                    key = "activate_level_7_talent_node",
                    label = "天赋节点激活按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    hint_client_x = 673.368225,
                    hint_client_y = 689.801453,
                    hint_ratio_x = 0.467942,
                    hint_ratio_y = 0.766446,
                    hint_max_distance = 80,
                    wait_after_ms = 650
                },
                {
                    key = "back_from_talent_detail",
                    label = "天赋返回按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
                    },
                    hint_client_x = 1373.452393,
                    hint_client_y = 52.000000,
                    hint_ratio_x = 0.954449,
                    hint_ratio_y = 0.057778,
                    hint_max_distance = 45,
                    wait_after_ms = 500
                }
            }
        }
    },
    skill_extra_by_level = {},
    talent_extra_by_level = {},
    contract_by_level = {}
}

M.AUTO_EQUIP_MAINTENANCE_CONFIG = {
    enabled = true,
    execute_ui = true,
    trigger_after_post_combat_loot = true,
    priority_over_level_up = true,
    periodic_scan_enabled = false,
    after_loot_timeout_ms = 30000,
    scan_interval_ms = 45000,
    retry_ms = 12000,
    min_hp_ratio = 0.72,
    allow_low_hp_maintenance = false,
    force_open_bag_after_loot_ignores_hp_and_monsters = true,
    combat_pulse_while_waiting_after_loot = true,
    allow_position_available_without_main_interface = true,
    safe_no_monster_ms = 1800,
    monster_guard_distance = 1000,
    monster_hard_block_distance = 300,
    nearby_monster_soft_observe_ms = 1800,
    nearby_monster_soft_resource_drop_epsilon = 1,
    open_bag_key_vk = 0x42,
    close_bag_key_vk = 0x42,
    close_bag_after_run = true,
    bag_open_wait_ms = 650,
    bag_close_wait_ms = 350,
    hover_wait_ms = 260,
    right_click_mouse_mode = "driver",
    right_click_foreground_wait_ms = 40,
    right_click_delay_ms = 50,
    equip_wait_ms = 650,
    ring_slot_selection_enabled = true,
    ring_slot_select_wait_ms = 450,
    ring_slot_left = {
        client_x = 972,
        client_y = 376
    },
    ring_slot_right = {
        client_x = 1376,
        client_y = 371
    },
    keep_equipped_rules = {
        {
            key = "firebirth_rings",
            item_type_patterns = { "戒指" },
            keep_names = { "火焰降生" },
            mode = "all_ring_slots",
            reason = "ring_keep_both_equipped",
            force_reason = "ring_force_firebirth_missing_slot"
        },
        {
            key = "firebirth_ring_force_equip",
            item_type_patterns = { "戒指" },
            keep_names = { "火焰降生" },
            mode = "candidate_ring_force_equip",
            preferred_slots = { "right", "left" },
            direct_equip_when_no_rings = true,
            reason = "ring_force_firebirth"
        },
        {
            key = "survival_belt",
            item_type_patterns = { "腰带", "护腰" },
            keep_names = { "求生之欲" },
            keep_name_match_mode = "contains",
            mode = "any_equipped",
            reason = "belt_keep_equipped"
        },
        {
            key = "lost_time_boots",
            item_type_patterns = { "鞋", "靴", "脚部", "足部" },
            keep_names = { "失期" },
            keep_name_match_mode = "contains",
            mode = "any_equipped",
            reason = "boots_keep_lost_time"
        },
        {
            key = "rock_lizard_head_same_name",
            item_type_patterns = { "头盔", "头部" },
            keep_names = { "岩石巨蜥之颅" },
            mode = "same_keep_name_only",
            reason = "head_keep_rock_lizard_same_name_only"
        },
        {
            key = "firebirth_ring_hand",
            item_type_patterns = { "戒指" },
            keep_names = { "火焰降生" },
            mode = "ring_slot_lock",
            reason = "ring_keep_firebirth_hand",
            force_reason = "ring_force_firebirth_missing_slot"
        }
    },
    keep_equipped_panel_max_x = 650,
    keep_equipped_marker_match_max_dx = 180,
    keep_equipped_marker_match_max_dy = 100,
    skip_non_two_hand_weapons = false,
    weapon_type_patterns = {
        "单手",
        "双手",
        "主手",
        "副手",
        "剑",
        "斧",
        "锤",
        "杖",
        "弓",
        "枪",
        "盾",
        "刀",
        "爪",
        "匕",
        "弩",
        "拳",
        "炮",
        "法器"
    },
    two_hand_weapon_type_patterns = { "双手" },
    identify_all_on_bag_open = true,
    identify_all_before_scan = true,
    identify_all_wait_ms = 800,
    identify_all_retry_attempts = 5,
    identify_all_retry_wait_ms = 300,
    identify_all_button_pattern = "pcbag_c.widgettree.pcbagmain.widgettree.pcuigridlistview.widgettree.uibutton_onekey",
    identify_all_button_fallback = {
        client_x = 1264.059570,
        client_y = 850.707092
    },
    bag_close_button_pattern = "pcbag_c.widgettree.pcbagmain.widgettree.uibutton_close",
    scan_max_items = 32,
    max_equips_per_run = 10,
    min_survival_gain = 0,
    min_damage_gain = 0,
    allow_damage_upgrade_when_survival_equal = true,
    bag_grid = {
        center_scan = true,
        first_center_x = 955,
        first_center_y = 571,
        last_center_x = 1390,
        last_center_y = 756,
        columns = 8,
        rows = 4,
        hover_jitter_px = 2,
        min_x = 880,
        max_x = 1395,
        min_y = 550,
        max_y = 790
    }
}

M.EQUIP_RECYCLE_MAINTENANCE_CONFIG = {
    enabled = true,
    execute_ui = true,
    trigger_after_auto_equip = true,
    priority_over_level_up = true,
    after_auto_equip_timeout_ms = 12000,
    open_bag_key_vk = 0x42,
    close_bag_key_vk = 0x42,
    bag_open_wait_ms = 650,
    bag_close_wait_ms = 350,
    bag_verify_attempts = 3,
    step_wait_ms = 350,
    confirm_wait_ms = 650,
    button_retry_attempts = 5,
    button_retry_wait_ms = 300,
    recycle_button_pattern = "pcbag_c.widgettree.pcbagmain.widgettree.pcuigridlistview.widgettree.uibutton_recycle",
    recycle_execute_button_pattern = "pcbag_c.widgettree.pcbagmain.widgettree.pcuigridlistview.widgettree.uibutton_recycle",
    rarity_filter_button_pattern = "pcbagfilterrarityitem.widgettree.selectbtn0",
    confirm_button_pattern = "confirmv2_c.widgettree.combuttonv2.widgettree.btn",
    bag_close_button_pattern = "pcbag_c.widgettree.pcbagmain.widgettree.uibutton_close",
    require_bag_open_for_button_fallback = true,
    button_fallbacks = {
        recycle_open = {
            client_x = 1348.82,
            client_y = 850.71
        },
        rarity_filter_0 = {
            client_x = 911.132019,
            client_y = 849.165894
        },
        recycle_execute = {
            client_x = 1348.82,
            client_y = 850.71
        },
        confirm = {
            client_x = 734.516785,
            client_y = 609.795044
        }
    },
    random_click_count = 1,
    random_click_rect = {
        min_x = 981,
        max_x = 1274,
        min_y = 87,
        max_y = 240
    }
}

M.POST_COMBAT_LOOT_CONFIG = {
    enabled = true,
    boss_kite_enabled = true,
    duration_ms = 3000,
    max_duration_ms = 5500,
    press_interval_ms = 450,
    empty_settle_ms = 900
}

local function clone_plain_table(value)
    if type(value) ~= "table" then
        return value
    end

    local copy = {}
    for k, v in pairs(value) do
        copy[k] = clone_plain_table(v)
    end
    return copy
end

local function make_skill_add_panel_step()
    return {
        key = "open_skill_add_panel",
        label = "技能升级入口按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.HomePointItem.WidgetTree.AddPanelBtn"
        },
        hint_client_x = 1274.171631,
        hint_client_y = 42.707001,
        hint_ratio_x = 0.884841,
        hint_ratio_y = 0.047452,
        hint_max_distance = 80,
        wait_after_ms = 800
    }
end

local function strip_skill_image_plan_panel_steps(plan)
    if type(plan) ~= "table" or type(plan.steps) ~= "table" then
        return
    end

    local stripped_steps = {}
    local inserted_add_panel = false
    for _, step in ipairs(plan.steps) do
        local key = tostring(type(step) == "table" and step.key or "")
        if key ~= "open_skill_fast_entrance_menu"
            and key ~= "open_skill_panel"
            and key ~= "back_from_skill_panel"
        then
            if key == "click_skill_upgrade_image" and not inserted_add_panel then
                stripped_steps[#stripped_steps + 1] = make_skill_add_panel_step()
                inserted_add_panel = true
            end
            stripped_steps[#stripped_steps + 1] = step
        end
    end
    plan.steps = stripped_steps

    for _, step in ipairs(plan.steps) do
        if tostring(type(step) == "table" and step.key or "") == "click_skill_upgrade_image" then
            step.cleanup_back_before_finish = false
        end
    end
end

local SKILL_PRE_BAG_MAIN_TAB_PATTERN = "UIButton Transient.GameEngine.CoreGameInstance.PCBag_C.WidgetTree.PCBagMain.WidgetTree.PCUIGridListView.WidgetTree.PCBagTabButtonItem101.WidgetTree.Button_Tab"
local SKILL_PRE_BAG_RECYCLE_PATTERN = "UIButton Transient.GameEngine.CoreGameInstance.PCBag_C.WidgetTree.PCBagMain.WidgetTree.PCUIGridListView.WidgetTree.UIButton_Recycle"
local SKILL_PRE_BAG_RARITY_MAGIC_PATTERN = "UIButton Transient.GameEngine.CoreGameInstance.PCBag_C.WidgetTree.PCBagMain.WidgetTree.PCUIGridListView.WidgetTree.PCBagFilterRarityItem.WidgetTree.SelectBtn0"
local SKILL_PRE_BAG_CONFIRM_PATTERN = "UIButton Transient.GameEngine.CoreGameInstance.ConfirmV2_C.WidgetTree.ComButtonV2.WidgetTree.Btn"
local SKILL_PRE_BAG_MAIN_TAB_X = 910.659119
local SKILL_PRE_BAG_MAIN_TAB_Y = 538.710449
local SKILL_PRE_BAG_MAIN_TAB_RATIO_X = 0.632402
local SKILL_PRE_BAG_MAIN_TAB_RATIO_Y = 0.598567
local SKILL_PRE_BAG_RARITY_HINT_X = 811.774414
local SKILL_PRE_BAG_RARITY_HINT_Y = 907.487305
local SKILL_PRE_BAG_RARITY_HINT_RATIO_X = 0.563732
local SKILL_PRE_BAG_RARITY_HINT_RATIO_Y = 1.008319
local SKILL_PRE_BAG_RARITY_CLICK_X = 931.00
local SKILL_PRE_BAG_RARITY_CLICK_Y = 813.00
local SKILL_PRE_BAG_RARITY_CLICK_RATIO_X = 0.646528
local SKILL_PRE_BAG_RARITY_CLICK_RATIO_Y = 0.903333

local function make_skill_pre_bag_press_step(key, label, wait_after_ms)
    return {
        key = key,
        label = label,
        kind = "press_key",
        key_vk = 66,
        wait_after_ms = wait_after_ms or 450,
        keep_wait_after_ms = true
    }
end

local function make_skill_pre_bag_button_step(opts)
    opts = type(opts) == "table" and opts or {}
    return make_maintenance_locator_step({
        key = opts.key,
        label = opts.label,
        include_patterns = { opts.pattern },
        hint_client_x = opts.hint_client_x,
        hint_client_y = opts.hint_client_y,
        hint_ratio_x = opts.hint_ratio_x,
        hint_ratio_y = opts.hint_ratio_y,
        hint_max_distance = opts.hint_max_distance or 80,
        target_poll_count = opts.target_poll_count or 15,
        target_poll_interval_ms = opts.target_poll_interval_ms or 100,
        missing_target_means_step_done = opts.missing_target_means_step_done,
        missing_target_means_plan_done = opts.missing_target_means_plan_done,
        poll_missing_target_before_done = opts.poll_missing_target_before_done,
        fixed_fallback_client_x = opts.fixed_fallback_client_x,
        fixed_fallback_client_y = opts.fixed_fallback_client_y,
        fixed_fallback_ratio_x = opts.fixed_fallback_ratio_x,
        fixed_fallback_ratio_y = opts.fixed_fallback_ratio_y,
        fixed_fallback_prefer_ratio = opts.fixed_fallback_prefer_ratio,
        fixed_fallback_mouse_mode = opts.fixed_fallback_mouse_mode,
        fixed_fallback_click_delay_ms = opts.fixed_fallback_click_delay_ms,
        fixed_fallback_hover_delay_ms = opts.fixed_fallback_hover_delay_ms,
        keep_wait_after_ms = opts.keep_wait_after_ms,
        enforce_wait_after_ms = opts.enforce_wait_after_ms,
        post_click_sleep_ms = opts.post_click_sleep_ms,
        wait_after_ms = opts.wait_after_ms or 320
    })
end

local function make_skill_pre_bag_verify_step(opts)
    local step = make_skill_pre_bag_button_step(opts)
    step.kind = "verify_target"
    step.expected_present = opts.expected_present ~= false
    step.verify_timeout_ms = opts.verify_timeout_ms or 3000
    step.verify_poll_ms = opts.verify_poll_ms or 100
    step.disable_target_poll = true
    step.wait_after_ms = opts.wait_after_ms or 120
    return step
end

local function make_skill_pre_add_bag_cleanup_steps(prefix)
    prefix = tostring(prefix or "skill")
    return {
        make_skill_pre_bag_press_step(prefix .. "_pre_bag_open", "skill pre-add bag open", 450),
        make_skill_pre_bag_button_step({
            key = prefix .. "_pre_bag_main_tab",
            label = "skill pre-add bag main tab",
            pattern = SKILL_PRE_BAG_MAIN_TAB_PATTERN,
            hint_client_x = SKILL_PRE_BAG_MAIN_TAB_X,
            hint_client_y = SKILL_PRE_BAG_MAIN_TAB_Y,
            hint_ratio_x = SKILL_PRE_BAG_MAIN_TAB_RATIO_X,
            hint_ratio_y = SKILL_PRE_BAG_MAIN_TAB_RATIO_Y,
            hint_max_distance = 30,
            fixed_fallback_client_x = SKILL_PRE_BAG_MAIN_TAB_X,
            fixed_fallback_client_y = SKILL_PRE_BAG_MAIN_TAB_Y,
            fixed_fallback_ratio_x = SKILL_PRE_BAG_MAIN_TAB_RATIO_X,
            fixed_fallback_ratio_y = SKILL_PRE_BAG_MAIN_TAB_RATIO_Y,
            fixed_fallback_prefer_ratio = true,
            fixed_fallback_mouse_mode = "api",
            fixed_fallback_hover_delay_ms = 80,
            fixed_fallback_click_delay_ms = 50,
            wait_after_ms = 260
        }),
        make_skill_pre_bag_button_step({
            key = prefix .. "_pre_bag_recycle_open",
            label = "skill pre-add bag recycle open",
            pattern = SKILL_PRE_BAG_RECYCLE_PATTERN,
            hint_client_x = 1327.063965,
            hint_client_y = 909.301697,
            hint_ratio_x = 0.921572,
            hint_ratio_y = 1.010335,
            hint_max_distance = 30,
            wait_after_ms = 300
        }),
        make_skill_pre_bag_button_step({
            key = prefix .. "_pre_bag_magic_filter",
            label = "skill pre-add bag magic filter",
            pattern = SKILL_PRE_BAG_RARITY_MAGIC_PATTERN,
            hint_client_x = SKILL_PRE_BAG_RARITY_HINT_X,
            hint_client_y = SKILL_PRE_BAG_RARITY_HINT_Y,
            hint_ratio_x = SKILL_PRE_BAG_RARITY_HINT_RATIO_X,
            hint_ratio_y = SKILL_PRE_BAG_RARITY_HINT_RATIO_Y,
            hint_max_distance = 30,
            fixed_fallback_client_x = SKILL_PRE_BAG_RARITY_CLICK_X,
            fixed_fallback_client_y = SKILL_PRE_BAG_RARITY_CLICK_Y,
            fixed_fallback_ratio_x = SKILL_PRE_BAG_RARITY_CLICK_RATIO_X,
            fixed_fallback_ratio_y = SKILL_PRE_BAG_RARITY_CLICK_RATIO_Y,
            fixed_fallback_prefer_ratio = true,
            fixed_fallback_mouse_mode = "api",
            fixed_fallback_hover_delay_ms = 80,
            fixed_fallback_click_delay_ms = 50,
            wait_after_ms = 260
        }),
        make_skill_pre_bag_button_step({
            key = prefix .. "_pre_bag_recycle_execute",
            label = "skill pre-add bag recycle execute",
            pattern = SKILL_PRE_BAG_RECYCLE_PATTERN,
            hint_client_x = 1327.063965,
            hint_client_y = 909.301697,
            hint_ratio_x = 0.921572,
            hint_ratio_y = 1.010335,
            hint_max_distance = 30,
            target_poll_count = 5,
            target_poll_interval_ms = 100,
            missing_target_means_step_done = true,
            poll_missing_target_before_done = true,
            wait_after_ms = 420
        }),
        make_skill_pre_bag_button_step({
            key = prefix .. "_pre_bag_confirm_recycle",
            label = "skill pre-add bag confirm recycle",
            pattern = SKILL_PRE_BAG_CONFIRM_PATTERN,
            hint_client_x = 733.020325,
            hint_client_y = 604.376953,
            hint_ratio_x = 0.509042,
            hint_ratio_y = 0.670785,
            hint_max_distance = 40,
            target_poll_count = 5,
            target_poll_interval_ms = 100,
            missing_target_means_step_done = true,
            poll_missing_target_before_done = true,
            keep_wait_after_ms = true,
            post_click_sleep_ms = 1200,
            wait_after_ms = 1200
        }),
        make_skill_pre_bag_press_step(prefix .. "_pre_bag_close", "skill pre-add bag close", 450),
        make_skill_pre_bag_verify_step({
            key = prefix .. "_pre_bag_verify_closed",
            label = "skill pre-add bag verify closed",
            pattern = SKILL_PRE_BAG_MAIN_TAB_PATTERN,
            hint_client_x = SKILL_PRE_BAG_MAIN_TAB_X,
            hint_client_y = SKILL_PRE_BAG_MAIN_TAB_Y,
            hint_ratio_x = SKILL_PRE_BAG_MAIN_TAB_RATIO_X,
            hint_ratio_y = SKILL_PRE_BAG_MAIN_TAB_RATIO_Y,
            hint_max_distance = 30,
            expected_present = false,
            verify_timeout_ms = 3500,
            wait_after_ms = 180
        })
    }
end

local function mark_skill_pre_add_bag_cleanup(plan)
    if type(plan) == "table" then
        plan.skill_pre_add_bag_cleanup = true
    end
end

local function find_skill_pre_add_bag_cleanup_insert_index(plan)
    -- This cleanup must run before any skill/talent panel is opened. Several add-skill
    -- plans also start with ordinary skill-upgrade image steps, so inserting later can
    -- press B while the skill panel is active and fail the bag-open verification.
    return 1
end

local function insert_skill_pre_add_bag_cleanup_steps(plan, prefix)
    if type(plan) ~= "table" or type(plan.steps) ~= "table" then
        return
    end
    local marker_key = tostring(prefix or "skill") .. "_pre_bag_open"
    for _, step in ipairs(plan.steps) do
        if tostring(type(step) == "table" and step.key or "") == marker_key then
            return
        end
    end

    local steps = {}
    local insert_index = find_skill_pre_add_bag_cleanup_insert_index(plan)
    for i = 1, insert_index - 1 do
        steps[#steps + 1] = plan.steps[i]
    end
    for _, step in ipairs(make_skill_pre_add_bag_cleanup_steps(prefix)) do
        steps[#steps + 1] = step
    end
    for i = insert_index, #plan.steps do
        steps[#steps + 1] = plan.steps[i]
    end
    plan.steps = steps
end

local function apply_skill_pre_add_bag_cleanup_steps()
    for level, plan in pairs(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level or {}) do
        if type(plan) == "table" and plan.skill_pre_add_bag_cleanup == true then
            insert_skill_pre_add_bag_cleanup_steps(plan, "level_" .. tostring(level))
        end
    end
    for level, plan in pairs(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_extra_by_level or {}) do
        if type(plan) == "table" and plan.skill_pre_add_bag_cleanup == true then
            insert_skill_pre_add_bag_cleanup_steps(plan, "level_" .. tostring(level) .. "_extra")
        end
    end
end

strip_skill_image_plan_panel_steps(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])

do
    local default_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    default_skill_plan.key = "default_skill_upgrade"
    default_skill_plan.label = "默认技能：找图升级技能"
    default_skill_plan.default_skill_plan = true
    for _, step in ipairs(type(default_skill_plan.steps) == "table" and default_skill_plan.steps or {}) do
        if tostring(step.key or "") == "back_from_skill_panel" then
            step.missing_target_means_plan_done = true
        elseif tostring(step.key or "") == "click_skill_upgrade_image" then
            step.repeat_image_until_missing = true
            step.repeat_image_until_missing_max_count = 30
            step.repeat_image_until_missing_interval_ms = 180
            if type(step.image_preset) == "table" then
                step.image_preset.click_repeat_count = 1
                step.image_preset.repeat_until_missing = true
                step.image_preset.repeat_until_missing_max_count = 30
                step.image_preset.repeat_until_missing_interval_ms = 180
            end
        end
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.default_skill_plan = default_skill_plan
end

do
    local level_8_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_8_skill_plan.key = "level_8_skill_upgrade_sequence"
    level_8_skill_plan.label = "8级技能：找图升级技能"
    for _, step in ipairs(type(level_8_skill_plan.steps) == "table" and level_8_skill_plan.steps or {}) do
        if tostring(step.key or "") == "click_skill_upgrade_image" then
            step.missing_image_means_done = true
        end
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[8] = level_8_skill_plan

    local level_9_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_9_skill_plan.key = "level_9_skill_upgrade_sequence"
    level_9_skill_plan.label = "9级技能：循环找图升级技能"
    for _, step in ipairs(type(level_9_skill_plan.steps) == "table" and level_9_skill_plan.steps or {}) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.missing_target_means_plan_done = true
        end
        if tostring(step.key or "") == "click_skill_upgrade_image" then
            step.missing_image_means_done = true
            step.missing_image_means_step_done = nil
            step.cleanup_back_before_finish = true
            step.repeat_image_until_missing = true
            step.repeat_image_until_missing_max_count = 30
            step.repeat_image_until_missing_interval_ms = 180
            if type(step.image_preset) == "table" then
                step.image_preset.click_repeat_count = 1
                step.image_preset.repeat_until_missing = true
                step.image_preset.repeat_until_missing_max_count = 30
                step.image_preset.repeat_until_missing_interval_ms = 180
            end
        end
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[9] = level_9_skill_plan

    local level_8_talent_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[7])
    level_8_talent_plan.key = "level_8_talent_node_activate"
    level_8_talent_plan.label = "8级天赋：激活天赋节点"
    local level_8_talent_steps = type(level_8_talent_plan.steps) == "table" and level_8_talent_plan.steps or {}
    for _, step in ipairs(level_8_talent_steps) do
        if tostring(step.key or "") == "select_level_7_talent_node" then
            step.key = "select_level_8_talent_node"
            step.hint_client_x = 447.221771
            step.hint_client_y = 614.126648
            step.hint_ratio_x = 0.310571
            step.hint_ratio_y = 0.681606
            step.hint_max_distance = 20
            step.fixed_fallback_client_x = 447.221771
            step.fixed_fallback_client_y = 614.126648
            step.fixed_fallback_ratio_x = 0.310571
            step.fixed_fallback_ratio_y = 0.681606
            step.fixed_fallback_prefer_ratio = true
            step.fixed_fallback_mouse_mode = "api"
            step.fixed_fallback_hover_delay_ms = 80
            step.fixed_fallback_click_delay_ms = 50
        elseif tostring(step.key or "") == "activate_level_7_talent_node" then
            step.key = "activate_level_8_talent_node"
        end
    end
    level_8_talent_plan.steps = level_8_talent_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[8] = level_8_talent_plan

    local level_9_talent_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[7])
    level_9_talent_plan.key = "level_9_talent_node_activate"
    level_9_talent_plan.label = "9级天赋：激活天赋节点"
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[9] = level_9_talent_plan

    local level_10_talent_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[7])
    level_10_talent_plan.key = "level_10_talent_node_activate"
    level_10_talent_plan.label = "10级天赋：激活天赋节点"
    for _, step in ipairs(type(level_10_talent_plan.steps) == "table" and level_10_talent_plan.steps or {}) do
        if tostring(step.key or "") == "select_level_7_talent_node" then
            step.key = "select_level_10_talent_node"
            step.hint_client_x = 603.846802
            step.hint_client_y = 616.538025
            step.hint_ratio_x = 0.419629
            step.hint_ratio_y = 0.685042
            step.hint_max_distance = 20
            step.wait_after_ms = 1200
        elseif tostring(step.key or "") == "activate_level_7_talent_node" then
            step.key = "activate_level_10_talent_node"
            step.hint_client_x = 814.298279
            step.hint_client_y = 672.801453
            step.hint_ratio_x = 0.565878
            step.hint_ratio_y = 0.747557
            step.hint_max_distance = 80
            step.wait_after_ms = 650
        end
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[10] = level_10_talent_plan

    local level_11_talent_plan = clone_plain_table(level_10_talent_plan)
    level_11_talent_plan.key = "level_11_talent_node_activate"
    level_11_talent_plan.label = "11级天赋：激活天赋节点"
    for _, step in ipairs(type(level_11_talent_plan.steps) == "table" and level_11_talent_plan.steps or {}) do
        if tostring(step.key or "") == "select_level_10_talent_node" then
            step.key = "select_level_11_talent_node"
        elseif tostring(step.key or "") == "activate_level_10_talent_node" then
            step.key = "activate_level_11_talent_node"
        end
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[11] = level_11_talent_plan

    local level_12_talent_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[7])
    level_12_talent_plan.key = "level_12_talent_node_activate"
    level_12_talent_plan.label = "12级天赋：激活天赋节点"
    for _, step in ipairs(type(level_12_talent_plan.steps) == "table" and level_12_talent_plan.steps or {}) do
        if tostring(step.key or "") == "select_level_7_talent_node" then
            step.key = "select_level_12_talent_node"
            step.hint_client_x = 759.776794
            step.hint_client_y = 477.651550
            step.hint_ratio_x = 0.527989
            step.hint_ratio_y = 0.530724
            step.hint_max_distance = 20
            step.wait_after_ms = 1200
        elseif tostring(step.key or "") == "activate_level_7_talent_node" then
            step.key = "activate_level_12_talent_node"
            step.hint_client_x = 589.579102
            step.hint_client_y = 672.801453
            step.hint_ratio_x = 0.409714
            step.hint_ratio_y = 0.747557
            step.hint_max_distance = 240
            step.wait_after_ms = 650
        end
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[12] = level_12_talent_plan

    local level_13_talent_plan = clone_plain_table(level_12_talent_plan)
    level_13_talent_plan.key = "level_13_talent_node_activate"
    level_13_talent_plan.label = "13级天赋：激活天赋节点"
    for _, step in ipairs(type(level_13_talent_plan.steps) == "table" and level_13_talent_plan.steps or {}) do
        if tostring(step.key or "") == "select_level_12_talent_node" then
            step.key = "select_level_13_talent_node"
            step.hint_client_x = 760.776794
            step.hint_client_y = 480.651550
            step.hint_ratio_x = 0.528684
            step.hint_ratio_y = 0.534057
            step.hint_max_distance = 20
        elseif tostring(step.key or "") == "activate_level_12_talent_node" then
            step.key = "activate_level_13_talent_node"
            step.hint_client_x = 590.579102
            step.hint_client_y = 675.801453
            step.hint_ratio_x = 0.410409
            step.hint_ratio_y = 0.750891
            step.hint_max_distance = 80
        end
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[13] = level_13_talent_plan

    local level_14_talent_plan = clone_plain_table(level_12_talent_plan)
    level_14_talent_plan.key = "level_14_talent_node_activate"
    level_14_talent_plan.label = "14级天赋：激活天赋节点"
    for _, step in ipairs(type(level_14_talent_plan.steps) == "table" and level_14_talent_plan.steps or {}) do
        if tostring(step.key or "") == "select_level_12_talent_node" then
            step.key = "select_level_14_talent_node"
        elseif tostring(step.key or "") == "activate_level_12_talent_node" then
            step.key = "activate_level_14_talent_node"
        end
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14] = level_14_talent_plan

    local function make_level_15_to_17_talent_plan(level)
        local plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[7])
        plan.key = string.format("level_%d_talent_node_activate", level)
        plan.label = string.format("%d级天赋：激活天赋节点", level)
        for _, step in ipairs(type(plan.steps) == "table" and plan.steps or {}) do
            if tostring(step.key or "") == "select_level_7_talent_node" then
                step.key = string.format("select_level_%d_talent_node", level)
                step.hint_client_x = 915.706848
                step.hint_client_y = 477.651550
                step.hint_ratio_x = 0.636349
                step.hint_ratio_y = 0.530724
                step.hint_max_distance = 20
                step.wait_after_ms = 1200
            elseif tostring(step.key or "") == "activate_level_7_talent_node" then
                step.key = string.format("activate_level_%d_talent_node", level)
                step.include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray",
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                }
                step.hint_client_x = 745.509155
                step.hint_client_y = 672.801453
                step.hint_ratio_x = 0.518074
                step.hint_ratio_y = 0.747557
                step.hint_max_distance = 80
                step.wait_after_ms = 650
            end
        end
        return plan
    end

    for _, level in ipairs({ 15, 16, 17 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_15_to_17_talent_plan(level)
    end

    for _, step in ipairs(type(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[15].steps) == "table" and M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[15].steps or {}) do
        if tostring(step.key or "") == "select_level_15_talent_node" then
            step.hint_client_x = 916.706848
            step.hint_client_y = 480.651550
            step.hint_ratio_x = 0.637044
            step.hint_ratio_y = 0.534057
            step.hint_max_distance = 20
        elseif tostring(step.key or "") == "activate_level_15_talent_node" then
            step.include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
            }
            step.hint_client_x = 746.509155
            step.hint_client_y = 675.801453
            step.hint_ratio_x = 0.518769
            step.hint_ratio_y = 0.750891
            step.hint_max_distance = 80
        end
    end

    local function make_level_18_to_20_talent_plan(level)
        local plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[7])
        plan.key = string.format("level_%d_talent_node_activate", level)
        plan.label = string.format("%d级天赋：激活天赋节点", level)
        for _, step in ipairs(type(plan.steps) == "table" and plan.steps or {}) do
            if tostring(step.key or "") == "select_level_7_talent_node" then
                step.key = string.format("select_level_%d_talent_node", level)
                step.hint_client_x = 1083.636841
                step.hint_client_y = 696.981323
                step.hint_ratio_x = 0.753049
                step.hint_ratio_y = 0.774424
                step.hint_max_distance = 20
                step.wait_after_ms = 1200
            elseif tostring(step.key or "") == "activate_level_7_talent_node" then
                step.key = string.format("activate_level_%d_talent_node", level)
                step.include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray",
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                }
                step.hint_client_x = 913.439148
                step.hint_client_y = 683.801453
                step.hint_ratio_x = 0.634774
                step.hint_ratio_y = 0.759779
                step.hint_max_distance = 80
                step.wait_after_ms = 650
            end
        end
        return plan
    end

    for _, level in ipairs({ 18, 19, 20 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_18_to_20_talent_plan(level)
    end

    local function find_plan_step(steps, key)
        for _, step in ipairs(type(steps) == "table" and steps or {}) do
            if tostring(step.key or "") == key then
                return step
            end
        end
        return nil
    end

    local function clone_step_with_key(step, key, label)
        local copy = clone_plain_table(step)
        if type(copy) ~= "table" then
            copy = {}
        end
        copy.key = key
        if label ~= nil then
            copy.label = label
        end
        return copy
    end

    local function extra_talent_fixed_click_step(key, label, client_x, client_y, ratio_x, ratio_y, wait_after_ms)
        return make_maintenance_fixed_click_step({
            key = key,
            label = label,
            fixed_client_x = client_x,
            fixed_client_y = client_y,
            fixed_ratio_x = ratio_x,
            fixed_ratio_y = ratio_y,
            wait_after_ms = wait_after_ms or 650
        })
    end

    local function maintenance_locator_step(key, label, include_patterns, hint_client_x, hint_client_y, hint_ratio_x, hint_ratio_y, hint_max_distance, wait_after_ms)
        return make_maintenance_locator_step({
            key = key,
            label = label,
            include_patterns = include_patterns,
            hint_client_x = hint_client_x,
            hint_client_y = hint_client_y,
            hint_ratio_x = hint_ratio_x,
            hint_ratio_y = hint_ratio_y,
            hint_max_distance = hint_max_distance or 80,
            wait_after_ms = wait_after_ms or 650
        })
    end

    local function apply_locator_fixed_fallback(step, client_x, client_y, ratio_x, ratio_y)
        if type(step) ~= "table" then
            return step
        end
        step.target_poll_count = 30
        step.target_poll_interval_ms = 100
        step.fixed_fallback_client_x = client_x
        step.fixed_fallback_client_y = client_y
        step.fixed_fallback_ratio_x = ratio_x
        step.fixed_fallback_ratio_y = ratio_y
        step.fixed_fallback_prefer_ratio = true
        step.fixed_fallback_mouse_mode = "api"
        step.fixed_fallback_click_delay_ms = 50
        return step
    end

    local level_20_plan = make_level_18_to_20_talent_plan(20)
    local level_20_base_steps = type(level_20_plan.steps) == "table" and level_20_plan.steps or {}
    local open_menu_step = find_plan_step(level_20_base_steps, "open_fast_entrance_menu")
    local open_talent_step = find_plan_step(level_20_base_steps, "open_talent_panel")
    local check_points_step = find_plan_step(level_20_base_steps, "check_talent_points")
    local back_step = find_plan_step(level_20_base_steps, "back_from_talent_detail")
    local level_20_steps = {}

    for _, step in ipairs(level_20_base_steps) do
        local key = tostring(step.key or "")
        local cloned = clone_plain_table(step)
        if key == "open_fast_entrance_menu" then
            cloned.key = "level_20_main_open_fast_entrance_menu"
            cloned.label = "20级天赋：重新打开菜单"
        elseif key == "open_talent_panel" then
            cloned.key = "level_20_main_open_talent_panel"
            cloned.label = "20级天赋：重新打开天赋面板"
        elseif key == "check_talent_points" then
            cloned.key = "level_20_main_check_talent_points"
            cloned.label = "20级天赋：重新检查天赋点"
        end
        level_20_steps[#level_20_steps + 1] = cloned
        if key == "activate_level_20_talent_node" then
            level_20_steps[#level_20_steps + 1] = maintenance_locator_step(
                "select_level_20_second_talent_node",
                "20级天赋：选择第二个天赋节点",
                {
                    "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                },
                1239.566772,
                696.981323,
                0.861408,
                0.774424,
                20,
                1200
            )
            level_20_steps[#level_20_steps + 1] = maintenance_locator_step(
                "activate_level_20_second_talent_node",
                "20级天赋：激活第二个天赋节点",
                {
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                },
                1069.369263,
                683.801453,
                0.743134,
                0.759779,
                80,
                650
            )
        end
    end

    level_20_plan.key = "level_20_talent_node_activate"
    level_20_plan.label = "20级天赋：激活天赋节点"
    level_20_plan.steps = level_20_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[20] = level_20_plan

    local level_21_open_menu_step = clone_step_with_key(
        open_menu_step,
        "level_21_open_fast_entrance_menu",
        "21级天赋：打开菜单"
    )
    level_21_open_menu_step.missing_target_means_step_done = true

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[21] = {
        key = "level_21_talent_category_and_node_activate",
        label = "21级天赋：激活天赋大类并点天赋节点",
        require_available_points = "defer",
        close_with_escape = false,
        steps = {
            level_21_open_menu_step,
            clone_step_with_key(open_talent_step, "level_21_open_talent_panel", "21级天赋：打开天赋面板"),
            clone_step_with_key(check_points_step, "level_21_check_talent_points", "21级天赋：检查天赋点"),
            maintenance_locator_step(
                "select_level_21_talent_category",
                "21级天赋：选择天赋大类",
                {
                    "UIButton Transient.GameEngine.CoreGameInstance.UICareerPointItem_C.WidgetTree.SelectBtn"
                },
                1055.438477,
                480.168762,
                0.733453,
                0.533521,
                80,
                900
            ),
            maintenance_locator_step(
                "activate_level_21_talent_category",
                "21级天赋：激活天赋大类",
                {
                    "UIButton Transient.GameEngine.CoreGameInstance.TipCareerItem_C.WidgetTree.ActiveBtn"
                },
                1256.157104,
                484.037933,
                0.872938,
                0.537820,
                80,
                900
            ),
            maintenance_locator_step(
                "select_level_21_talent_node",
                "21级天赋：选择天赋节点",
                {
                    "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                },
                303.986755,
                419.208282,
                0.211249,
                0.465787,
                20,
                1200
            ),
            maintenance_locator_step(
                "activate_level_21_talent_node",
                "21级天赋：激活天赋节点",
                {
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                },
                514.438232,
                683.801453,
                0.357497,
                0.759779,
                80,
                650
            ),
            clone_step_with_key(back_step, "level_21_back_from_talent_panel", "21级天赋：返回")
        }
    }

    local function make_level_22_plus_talent_plan(level, node_x, node_y, node_ratio_x, node_ratio_y, activate_x, activate_y, activate_ratio_x, activate_ratio_y)
        local level_open_menu_step = clone_step_with_key(
            open_menu_step,
            "level_" .. tostring(level) .. "_open_fast_entrance_menu",
            tostring(level) .. "级天赋：打开菜单"
        )
        level_open_menu_step.missing_target_means_step_done = true

        return {
            key = "level_" .. tostring(level) .. "_talent_node_activate",
            label = tostring(level) .. "级天赋：激活天赋节点",
            require_available_points = "defer",
            close_with_escape = false,
            steps = {
                level_open_menu_step,
                clone_step_with_key(open_talent_step, "level_" .. tostring(level) .. "_open_talent_panel", tostring(level) .. "级天赋：打开天赋面板"),
                clone_step_with_key(check_points_step, "level_" .. tostring(level) .. "_check_talent_points", tostring(level) .. "级天赋：检查天赋点"),
                maintenance_locator_step(
                    "select_level_" .. tostring(level) .. "_talent_node",
                    tostring(level) .. "级天赋：选择天赋节点",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    node_x,
                    node_y,
                    node_ratio_x,
                    node_ratio_y,
                    20,
                    1200
                ),
                maintenance_locator_step(
                    "activate_level_" .. tostring(level) .. "_talent_node",
                    tostring(level) .. "级天赋：激活天赋节点",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                    },
                    activate_x,
                    activate_y,
                    activate_ratio_x,
                    activate_ratio_y,
                    80,
                    650
                ),
                clone_step_with_key(back_step, "level_" .. tostring(level) .. "_back_from_talent_panel", tostring(level) .. "级天赋：返回")
            }
        }
    end

    for _, level in ipairs({ 22, 23 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_22_plus_talent_plan(
            level,
            303.986755,
            419.208282,
            0.211249,
            0.465787,
            514.438232,
            683.801453,
            0.357497,
            0.759779
        )
    end

    for _, level in ipairs({ 24, 25, 26 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_22_plus_talent_plan(
            level,
            459.916748,
            419.208282,
            0.319609,
            0.465787,
            670.368225,
            683.801453,
            0.465857,
            0.759779
        )
    end

    for _, level in ipairs({ 29, 30, 31 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_22_plus_talent_plan(
            level,
            456.916748,
            621.538025,
            0.317524,
            0.690598,
            667.368225,
            677.801453,
            0.463772,
            0.753113
        )
    end

    local level_32_talent_plan = make_level_22_plus_talent_plan(
        32,
        768.776794,
        482.651550,
        0.534244,
        0.536280,
        598.579102,
        677.801453,
        0.415969,
        0.753113
    )
    local level_32_talent_steps = type(level_32_talent_plan.steps) == "table" and level_32_talent_plan.steps or {}
    local level_32_back_step = table.remove(level_32_talent_steps, #level_32_talent_steps)
    level_32_talent_steps[#level_32_talent_steps + 1] = maintenance_locator_step(
        "activate_level_32_talent_node_repeat_2",
        "32级天赋：第二次激活天赋节点",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
            "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
        },
        598.579102,
        677.801453,
        0.415969,
        0.753113,
        80,
        650
    )
    level_32_talent_steps[#level_32_talent_steps + 1] = maintenance_locator_step(
        "activate_level_32_talent_node_repeat_3",
        "32级天赋：第三次激活天赋节点",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
            "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
        },
        598.579102,
        677.801453,
        0.415969,
        0.753113,
        80,
        650
    )
    if type(level_32_back_step) == "table" then
        level_32_talent_steps[#level_32_talent_steps + 1] = level_32_back_step
    end
    level_32_talent_plan.key = "level_32_talent_node_activate_triple"
    level_32_talent_plan.label = "32级天赋：激活天赋节点三次"
    level_32_talent_plan.steps = level_32_talent_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[32] = level_32_talent_plan

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[33] = make_level_22_plus_talent_plan(
        33,
        924.706848,
        482.651550,
        0.642604,
        0.536280,
        754.509155,
        677.801453,
        0.524329,
        0.753113
    )

    for _, level in ipairs({ 34, 35, 36 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_22_plus_talent_plan(
            level,
            924.706848,
            413.208282,
            0.642604,
            0.459120,
            754.509155,
            677.801453,
            0.524329,
            0.753113
        )
    end

    for _, level in ipairs({ 37, 38, 39 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_22_plus_talent_plan(
            level,
            1080.636841,
            413.208282,
            0.750964,
            0.459120,
            910.439148,
            677.801453,
            0.632689,
            0.753113
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[40] = make_level_22_plus_talent_plan(
        40,
        1236.566772,
        413.208282,
        0.859324,
        0.459120,
        1066.369263,
        677.801453,
        0.741049,
        0.753113
    )

    local level_41_open_menu_step = clone_step_with_key(
        open_menu_step,
        "level_41_open_fast_entrance_menu",
        "41级天赋：打开菜单"
    )
    level_41_open_menu_step.missing_target_means_step_done = true
    local level_41_select_category_step = make_maintenance_fixed_click_step({
        key = "select_level_41_talent_category",
        label = "41级天赋：固定点击天赋大类",
        fixed_client_x = 1071.00,
        fixed_client_y = 522.00,
        fixed_ratio_x = 0.744267,
        fixed_ratio_y = 0.580000,
        wait_after_ms = 900
    })
    local level_41_activate_category_step = make_maintenance_fixed_click_step({
        key = "activate_level_41_talent_category",
        label = "41级天赋：固定点击天赋大类激活",
        fixed_client_x = 1296.00,
        fixed_client_y = 479.00,
        fixed_ratio_x = 0.900625,
        fixed_ratio_y = 0.532222,
        wait_after_ms = 900
    })
    local level_41_activate_node_step = maintenance_locator_step(
        "activate_level_41_talent_node",
        "41级天赋：连续激活天赋节点",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
            "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
        },
        900.439148,
        672.801453,
        0.625740,
        0.747557,
        80,
        650
    )
    level_41_activate_node_step.click_repeat_count = 3
    level_41_activate_node_step.click_repeat_interval_ms = 180
    local level_41_select_node_step = apply_locator_fixed_fallback(maintenance_locator_step(
        "select_level_41_talent_node",
        "41级天赋：选择天赋节点",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
        },
        1073.375366,
        616.626709,
        0.745400,
        0.685141,
        20,
        1200
    ), 1073.375366, 616.626709, 0.745400, 0.685141)

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[41] = {
        key = "level_41_talent_category_tab_and_node_activate",
        label = "41级天赋：激活天赋大类并点天赋节点",
        require_available_points = "defer",
        close_with_escape = false,
        steps = {
            level_41_open_menu_step,
            clone_step_with_key(open_talent_step, "level_41_open_talent_panel", "41级天赋：打开天赋面板"),
            clone_step_with_key(check_points_step, "level_41_check_talent_points", "41级天赋：检查天赋点"),
            level_41_select_category_step,
            level_41_activate_category_step,
            maintenance_locator_step(
                "select_level_41_second_talent_card",
                "41级天赋：切换第二张天赋卡",
                {
                    "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                },
                47.726845,
                392.616241,
                0.033167,
                0.436240,
                80,
                900
            ),
            level_41_select_node_step,
            level_41_activate_node_step,
            clone_step_with_key(back_step, "level_41_back_from_talent_panel", "41级天赋：返回")
        }
    }

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[42] = make_level_22_plus_talent_plan(
        42,
        1226.566772,
        616.538025,
        0.852374,
        0.685042,
        1056.369263,
        672.801453,
        0.734100,
        0.747557
    )

    local level_43_talent_plan = make_level_22_plus_talent_plan(
        43,
        914.706848,
        547.094788,
        0.635655,
        0.607883,
        744.509155,
        672.801453,
        0.517380,
        0.747557
    )
    local level_43_talent_steps = type(level_43_talent_plan.steps) == "table" and level_43_talent_plan.steps or {}
    local level_43_back_step = table.remove(level_43_talent_steps, #level_43_talent_steps)
    level_43_talent_steps[#level_43_talent_steps + 1] = make_maintenance_fixed_click_step({
        key = "level_43_fixed_click_extra_talent_388_251",
        label = "43级天赋：额外固定点击1",
        fixed_client_x = 388.00,
        fixed_client_y = 251.00,
        fixed_ratio_x = 0.269632,
        fixed_ratio_y = 0.278889,
        wait_after_ms = 900
    })
    level_43_talent_steps[#level_43_talent_steps + 1] = make_maintenance_fixed_click_step({
        key = "level_43_fixed_click_extra_talent_341_617",
        label = "43级天赋：额外固定点击2",
        fixed_client_x = 341.00,
        fixed_client_y = 617.00,
        fixed_ratio_x = 0.236970,
        fixed_ratio_y = 0.685556,
        wait_after_ms = 900
    })
    level_43_talent_steps[#level_43_talent_steps + 1] = make_maintenance_fixed_click_step({
        key = "level_43_fixed_click_extra_talent_1277_148",
        label = "43级天赋：额外固定点击3",
        fixed_client_x = 1277.00,
        fixed_client_y = 148.00,
        fixed_ratio_x = 0.887422,
        fixed_ratio_y = 0.164444,
        wait_after_ms = 900
    })
    if type(level_43_back_step) == "table" then
        level_43_talent_steps[#level_43_talent_steps + 1] = level_43_back_step
    end
    level_43_talent_plan.key = "level_43_talent_node_activate_with_extra_category_clicks"
    level_43_talent_plan.label = "43级天赋：激活天赋节点并额外点击大类"
    level_43_talent_plan.steps = level_43_talent_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[43] = level_43_talent_plan

    for _, level in ipairs({ 44, 45 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_22_plus_talent_plan(
            level,
            914.706848,
            547.094788,
            0.635655,
            0.607883,
            744.509155,
            672.801453,
            0.517380,
            0.747557
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[46] = make_level_22_plus_talent_plan(
        46,
        1070.636841,
        547.094788,
        0.744014,
        0.607883,
        900.439148,
        672.801453,
        0.625740,
        0.747557
    )

    local level_47_talent_plan = make_level_22_plus_talent_plan(
        47,
        920.706848,
        415.208282,
        0.639824,
        0.461343,
        750.509155,
        679.801453,
        0.521549,
        0.755335
    )
    local level_47_talent_steps = type(level_47_talent_plan.steps) == "table" and level_47_talent_plan.steps or {}
    table.insert(level_47_talent_steps, 4, maintenance_locator_step(
        "select_level_47_trickster_talent_card",
        "47级天赋：切换欺诈之神天赋卡",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
        },
        53.726845,
        344.195770,
        0.037336,
        0.382440,
        80,
        900
    ))
    level_47_talent_plan.key = "level_47_trickster_card_and_talent_node_activate"
    level_47_talent_plan.label = "47级天赋：切换欺诈之神并激活天赋节点"
    level_47_talent_plan.steps = level_47_talent_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[47] = level_47_talent_plan

    for _, level in ipairs({ 48, 49 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_22_plus_talent_plan(
            level,
            920.706848,
            415.208282,
            0.639824,
            0.461343,
            750.509155,
            679.801453,
            0.521549,
            0.755335
        )
    end

    for _, level in ipairs({ 50, 51, 52 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_22_plus_talent_plan(
            level,
            1089.636841,
            426.208282,
            0.757218,
            0.473565,
            919.439148,
            690.801453,
            0.638943,
            0.767557
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[53] = make_level_22_plus_talent_plan(
        53,
        1245.566772,
        426.208282,
        0.865578,
        0.473565,
        1075.369263,
        690.801453,
        0.747303,
        0.767557
    )

    local level_54_talent_plan = make_level_22_plus_talent_plan(
        54,
        621.846802,
        426.208282,
        0.432138,
        0.473565,
        832.298279,
        690.801453,
        0.578387,
        0.767557
    )
    for _, step in ipairs(type(level_54_talent_plan.steps) == "table" and level_54_talent_plan.steps or {}) do
        if tostring(step.key or "") == "activate_level_54_talent_node" then
            step.label = "54级天赋：激活天赋节点两次"
            step.click_repeat_count = 2
            step.click_repeat_interval_ms = 180
            break
        end
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[54] = level_54_talent_plan

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[55] = make_level_22_plus_talent_plan(
        55,
        621.846802,
        426.208282,
        0.432138,
        0.473565,
        832.298279,
        690.801453,
        0.578387,
        0.767557
    )

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[56] = make_level_22_plus_talent_plan(
        56,
        777.776794,
        426.208282,
        0.540498,
        0.473565,
        607.579102,
        690.801453,
        0.422223,
        0.767557
    )

    for _, level in ipairs({ 57, 58, 59 }) do
        local plan = make_level_22_plus_talent_plan(
            level,
            309.986755,
            426.208282,
            0.215418,
            0.473565,
            520.438232,
            690.801453,
            0.361667,
            0.767557
        )
        local steps = type(plan.steps) == "table" and plan.steps or {}
        table.insert(steps, 4, maintenance_locator_step(
            "select_level_" .. tostring(level) .. "_psychic_talent_card",
            tostring(level) .. "级天赋：切换异能者天赋卡",
            {
                "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem3.WidgetTree.TabBtn"
            },
            66.726845,
            466.036713,
            0.046370,
            0.517819,
            80,
            900
        ))
        plan.key = "level_" .. tostring(level) .. "_psychic_card_and_talent_node_activate"
        plan.label = tostring(level) .. "级天赋：切换异能者并激活天赋节点"
        plan.steps = steps
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = plan
    end

    local original_talent_by_level = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level)

    local function safe_step_key(value)
        local text = tostring(value or "step")
        text = text:gsub("[^%w_]", "_")
        if text == "" then
            text = "step"
        end
        return text
    end

    local function clone_shifted_step(step, level, index, suffix)
        local cloned = clone_plain_table(step)
        if type(cloned) ~= "table" then
            cloned = {}
        end
        cloned.key = string.format(
            "level_%d_shift_%02d_%s",
            tonumber(level) or 0,
            tonumber(index) or 0,
            safe_step_key(suffix or cloned.key or "step")
        )
        return cloned
    end

    local function retarget_shifted_talent_plan(source_plan, level, source_level)
        local plan = clone_plain_table(source_plan)
        if type(plan) ~= "table" then
            return nil
        end
        plan.key = string.format("level_%d_talent_shift_from_level_%d", tonumber(level) or 0, tonumber(source_level) or 0)
        plan.label = string.format("%d级天赋：执行原%d级补位配置", tonumber(level) or 0, tonumber(source_level) or 0)
        local retargeted_steps = {}
        for index, step in ipairs(type(plan.steps) == "table" and plan.steps or {}) do
            retargeted_steps[#retargeted_steps + 1] = clone_shifted_step(step, level, index, step.key)
        end
        plan.steps = retargeted_steps
        return plan
    end

    local function find_step_by_key_contains(plan, needle)
        local steps = type(plan) == "table" and type(plan.steps) == "table" and plan.steps or {}
        for _, step in ipairs(steps) do
            if tostring(step.key or ""):find(tostring(needle or ""), 1, true) ~= nil then
                return step
            end
        end
        return nil
    end

    local function append_shifted_step(steps, level, source_step, suffix)
        if type(source_step) ~= "table" then
            return
        end
        steps[#steps + 1] = clone_shifted_step(source_step, level, #steps + 1, suffix or source_step.key)
    end

    local function append_shifted_setup(steps, level, base_plan)
        append_shifted_step(steps, level, find_step_by_key_contains(base_plan, "open_fast_entrance_menu"), "open_fast_entrance_menu")
        append_shifted_step(steps, level, find_step_by_key_contains(base_plan, "open_talent_panel"), "open_talent_panel")
        append_shifted_step(steps, level, find_step_by_key_contains(base_plan, "check_talent_points"), "check_talent_points")
    end

    local function append_shifted_back(steps, level, base_plan)
        append_shifted_step(steps, level, find_step_by_key_contains(base_plan, "back_from_talent"), "back_from_talent")
    end

    local function clone_activation_step_for_repeat(step, repeat_count)
        local cloned = clone_plain_table(step)
        if type(cloned) ~= "table" then
            cloned = {}
        end
        repeat_count = tonumber(repeat_count) or 1
        if repeat_count > 1 then
            cloned.click_repeat_count = repeat_count
            cloned.click_repeat_interval_ms = tonumber(cloned.click_repeat_interval_ms) or 180
        else
            cloned.click_repeat_count = nil
            cloned.click_repeat_interval_ms = nil
        end
        return cloned
    end

    local function make_composite_talent_plan(level, label, base_plan, build_steps)
        local plan = {
            key = string.format("level_%d_talent_shift_composite", tonumber(level) or 0),
            label = label,
            require_available_points = "defer",
            close_with_escape = false,
            steps = {}
        }
        append_shifted_setup(plan.steps, level, base_plan)
        if type(build_steps) == "function" then
            build_steps(plan.steps, level)
        end
        append_shifted_back(plan.steps, level, base_plan)
        return plan
    end

    local function make_manual_talent_plan(level, label, build_steps)
        local plan = {
            key = string.format("level_%d_talent_shift_manual", tonumber(level) or 0),
            label = label,
            require_available_points = "defer",
            close_with_escape = false,
            steps = {}
        }
        if type(build_steps) == "function" then
            build_steps(plan.steps, level)
        end
        return plan
    end

    local function append_shifted_plan_steps(steps, level, source_plan)
        for _, step in ipairs(type(source_plan) == "table" and type(source_plan.steps) == "table" and source_plan.steps or {}) do
            append_shifted_step(steps, level, step, step.key)
        end
    end

    local function append_shifted_exact_steps(steps, level, source_plan, keys)
        local source_steps = type(source_plan) == "table" and type(source_plan.steps) == "table" and source_plan.steps or {}
        for _, key in ipairs(type(keys) == "table" and keys or {}) do
            append_shifted_step(steps, level, find_plan_step(source_steps, key), key)
        end
    end

    -- Talent point order correction:
    -- level 4 must repeat the same first node as level 3. Later levels consume
    -- the previous point in the original sequence; multi-point levels are split
    -- across the following level when the shifted sequence crosses a boundary.
    -- Before the level-18 insertion below, level 60 has no talent plan after this correction.
    local explicit_level_8_talent_plan = clone_plain_table(original_talent_by_level[8])
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[4] =
        retarget_shifted_talent_plan(original_talent_by_level[3], 4, 3)
    for level = 5, 31 do
        if level ~= 8 then
            M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] =
                retarget_shifted_talent_plan(original_talent_by_level[level - 1], level, level - 1)
        end
    end
    if type(explicit_level_8_talent_plan) == "table" then
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[8] = explicit_level_8_talent_plan
    end

    local function clone_level_8_talent_plan_for_level(level)
        local plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[8])
        if type(plan) ~= "table" then
            return nil
        end
        plan.key = string.format("level_%d_talent_same_as_level_8_node_activate", tonumber(level) or 0)
        plan.label = string.format("%d级天赋：复用8级天赋节点配置", tonumber(level) or 0)
        plan.completed_alias_keys = nil
        for _, step in ipairs(type(plan.steps) == "table" and plan.steps or {}) do
            local step_key = tostring(step.key or "")
            if step_key ~= "" then
                step.key = step_key:gsub("level_8", "level_" .. tostring(tonumber(level) or 0))
            end
        end
        return plan
    end

    -- Keep levels 8/9/10 on the same sampled talent node configuration. Each
    -- level still gets a unique plan key so persistence remains per-level.
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[9] = clone_level_8_talent_plan_for_level(9)
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[10] = clone_level_8_talent_plan_for_level(10)
    do
        local level_10_talent_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[10]
        if type(level_10_talent_plan) == "table" then
            level_10_talent_plan.key = "level_10_talent_two_node_activate"
            level_10_talent_plan.label = "10级天赋：激活两个天赋节点"
            local level_10_steps = type(level_10_talent_plan.steps) == "table" and level_10_talent_plan.steps or {}
            local insert_at = #level_10_steps + 1
            for index, step in ipairs(level_10_steps) do
                if tostring(step.key or ""):find("back_from_talent", 1, true) ~= nil then
                    insert_at = index
                    break
                end
            end
            table.insert(level_10_steps, insert_at, clone_shifted_step({
                key = "select_level_10_second_talent_node",
                label = "10级第二组天赋节点按钮",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                },
                hint_client_x = 602.260132,
                hint_client_y = 615.126648,
                hint_ratio_x = 0.418236,
                hint_ratio_y = 0.682715,
                hint_max_distance = 30,
                fixed_fallback_client_x = 602.260132,
                fixed_fallback_client_y = 615.126648,
                fixed_fallback_ratio_x = 0.418236,
                fixed_fallback_ratio_y = 0.682715,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_hover_delay_ms = 80,
                fixed_fallback_click_delay_ms = 50,
                wait_after_ms = 1200
            }, 10, insert_at, "select_level_10_second_talent_node"))
            table.insert(level_10_steps, insert_at + 1, clone_shifted_step({
                key = "activate_level_10_second_talent_node",
                label = "10级第二组天赋节点激活按钮",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                },
                hint_client_x = 812.857910,
                hint_client_y = 671.429199,
                hint_ratio_x = 0.564485,
                hint_ratio_y = 0.745204,
                hint_max_distance = 30,
                fixed_fallback_client_x = 812.857910,
                fixed_fallback_client_y = 671.429199,
                fixed_fallback_ratio_x = 0.564485,
                fixed_fallback_ratio_y = 0.745204,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_hover_delay_ms = 80,
                fixed_fallback_click_delay_ms = 50,
                wait_after_ms = 650
            }, 10, insert_at + 1, "activate_level_10_second_talent_node"))
            level_10_talent_plan.steps = level_10_steps
        end
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[11] = make_composite_talent_plan(
        11,
        "11级天赋：激活指定天赋节点",
        original_talent_by_level[11],
        function(steps, level)
            append_shifted_step(steps, level, {
                key = "select_level_11_talent_node",
                label = "11级天赋节点按钮",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                },
                hint_client_x = 607.846802,
                hint_client_y = 618.538025,
                hint_ratio_x = 0.422409,
                hint_ratio_y = 0.687264,
                hint_max_distance = 20,
                wait_after_ms = 1200
            }, "select_level_11_talent_node")
            append_shifted_step(steps, level, {
                key = "activate_level_11_talent_node",
                label = "11级天赋节点激活按钮",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                },
                hint_client_x = 818.298279,
                hint_client_y = 674.801453,
                hint_ratio_x = 0.568658,
                hint_ratio_y = 0.749779,
                hint_max_distance = 80,
                wait_after_ms = 650
            }, "activate_level_11_talent_node")
        end
    )
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[11].completed_alias_keys = {
        "level_11_talent_shift_from_level_10",
        "level_11_talent_shift_from_level_11",
        "level_11_talent_node_activate"
    }
    local function make_level_12_to_14_same_talent_plan(level)
        local plan = make_composite_talent_plan(
            level,
            string.format("%d级天赋：激活同组天赋节点", tonumber(level) or 0),
            original_talent_by_level[level],
            function(steps, current_level)
                append_shifted_step(steps, current_level, {
                    key = string.format("select_level_%d_same_talent_node", tonumber(current_level) or 0),
                    label = string.format("%d级天赋节点按钮", tonumber(current_level) or 0),
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    hint_client_x = 763.776794,
                    hint_client_y = 479.651550,
                    hint_ratio_x = 0.530769,
                    hint_ratio_y = 0.532946,
                    hint_max_distance = 20,
                    wait_after_ms = 1200
                }, string.format("select_level_%d_same_talent_node", tonumber(current_level) or 0))
                append_shifted_step(steps, current_level, {
                    key = string.format("activate_level_%d_same_talent_node", tonumber(current_level) or 0),
                    label = string.format("%d级天赋节点激活按钮", tonumber(current_level) or 0),
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    hint_client_x = 593.579102,
                    hint_client_y = 674.801453,
                    hint_ratio_x = 0.412494,
                    hint_ratio_y = 0.749779,
                    hint_max_distance = 80,
                    wait_after_ms = 650
                }, string.format("activate_level_%d_same_talent_node", tonumber(current_level) or 0))
                if tonumber(current_level) == 12 then
                    append_shifted_step(steps, current_level, {
                        key = "select_level_12_keystone_tab",
                        label = "12级天赋基石页签按钮",
                        include_patterns = {
                            "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.KeyStoneItem1.WidgetTree.TabBtn"
                        },
                        hint_client_x = 357.321289,
                        hint_client_y = 250.351654,
                        hint_ratio_x = 0.248140,
                        hint_ratio_y = 0.277860,
                        hint_max_distance = 30,
                        fixed_fallback_client_x = 357.321289,
                        fixed_fallback_client_y = 250.351654,
                        fixed_fallback_ratio_x = 0.248140,
                        fixed_fallback_ratio_y = 0.277860,
                        fixed_fallback_prefer_ratio = true,
                        fixed_fallback_mouse_mode = "api",
                        fixed_fallback_hover_delay_ms = 80,
                        fixed_fallback_click_delay_ms = 50,
                        wait_after_ms = 650
                    }, "select_level_12_keystone_tab")
                    append_shifted_step(steps, current_level, {
                        key = "select_level_12_keystone_option_2",
                        label = "12级天赋基石2按钮",
                        include_patterns = {
                            "UIButton Transient.GameEngine.CoreGameInstance.TabKeyStoneItem_C.WidgetTree.SelectBtn2"
                        },
                        hint_client_x = 644.312500,
                        hint_client_y = 633.974976,
                        hint_ratio_x = 0.447439,
                        hint_ratio_y = 0.703635,
                        hint_max_distance = 30,
                        fixed_fallback_client_x = 644.312500,
                        fixed_fallback_client_y = 633.974976,
                        fixed_fallback_ratio_x = 0.447439,
                        fixed_fallback_ratio_y = 0.703635,
                        fixed_fallback_prefer_ratio = true,
                        fixed_fallback_mouse_mode = "api",
                        fixed_fallback_hover_delay_ms = 80,
                        fixed_fallback_click_delay_ms = 50,
                        wait_after_ms = 650
                    }, "select_level_12_keystone_option_2")
                end
            end
        )
        plan.key = string.format("level_%d_talent_fixed_763_593", tonumber(level) or 0)
        return plan
    end
    for _, level in ipairs({ 12, 13, 14 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_12_to_14_same_talent_plan(level)
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[20] = make_manual_talent_plan(
        20,
        "20级天赋：补位激活19级节点并执行20级前两段",
        function(steps, level)
            append_shifted_plan_steps(steps, level, original_talent_by_level[19])
            append_shifted_exact_steps(steps, level, original_talent_by_level[20], {
                "level_20_main_open_fast_entrance_menu",
                "level_20_main_open_talent_panel",
                "level_20_main_check_talent_points",
                "select_level_20_talent_node",
                "activate_level_20_talent_node",
                "back_from_talent_detail"
            })
        end
    )

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[21] = make_manual_talent_plan(
        21,
        "21级天赋：补位激活20级剩余第二节点",
        function(steps, level)
            append_shifted_exact_steps(steps, level, original_talent_by_level[20], {
                "level_20_main_open_fast_entrance_menu",
                "level_20_main_open_talent_panel",
                "level_20_main_check_talent_points",
                "select_level_20_second_talent_node",
                "activate_level_20_second_talent_node",
                "back_from_talent_detail"
            })
        end
    )

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[32] = make_composite_talent_plan(
        32,
        "32级天赋：补位激活31级节点并激活32级节点两次",
        original_talent_by_level[32],
        function(steps, level)
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[31], "select_level_31_talent_node"), "select_level_31_talent_node")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[31], "activate_level_31_talent_node"), "activate_level_31_talent_node")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[32], "select_level_32_talent_node"), "select_level_32_talent_node")
            append_shifted_step(steps, level, clone_activation_step_for_repeat(
                find_step_by_key_contains(original_talent_by_level[32], "activate_level_32_talent_node"),
                2
            ), "activate_level_32_talent_node_twice")
        end
    )

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[33] = make_composite_talent_plan(
        33,
        "33级天赋：补位激活32级节点第三次",
        original_talent_by_level[32],
        function(steps, level)
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[32], "select_level_32_talent_node"), "select_level_32_talent_node")
            append_shifted_step(steps, level, clone_activation_step_for_repeat(
                find_step_by_key_contains(original_talent_by_level[32], "activate_level_32_talent_node"),
                1
            ), "activate_level_32_talent_node_once")
        end
    )
    for level = 34, 40 do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] =
            retarget_shifted_talent_plan(original_talent_by_level[level - 1], level, level - 1)
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[41] = make_composite_talent_plan(
        41,
        "41级天赋：补位激活40级节点并激活41级节点两次",
        original_talent_by_level[41],
        function(steps, level)
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[40], "select_level_40_talent_node"), "select_level_40_talent_node")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[40], "activate_level_40_talent_node"), "activate_level_40_talent_node")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[41], "select_level_41_talent_category"), "select_level_41_talent_category")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[41], "activate_level_41_talent_category"), "activate_level_41_talent_category")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[41], "select_level_41_second_talent_card"), "select_level_41_second_talent_card")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[41], "select_level_41_talent_node"), "select_level_41_talent_node")
            append_shifted_step(steps, level, clone_activation_step_for_repeat(
                find_step_by_key_contains(original_talent_by_level[41], "activate_level_41_talent_node"),
                2
            ), "activate_level_41_talent_node_twice")
        end
    )

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[42] = make_composite_talent_plan(
        42,
        "42级天赋：补位激活41级节点第三次",
        original_talent_by_level[41],
        function(steps, level)
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[41], "select_level_41_talent_category"), "select_level_41_talent_category")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[41], "activate_level_41_talent_category"), "activate_level_41_talent_category")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[41], "select_level_41_second_talent_card"), "select_level_41_second_talent_card")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[41], "select_level_41_talent_node"), "select_level_41_talent_node")
            append_shifted_step(steps, level, clone_activation_step_for_repeat(
                find_step_by_key_contains(original_talent_by_level[41], "activate_level_41_talent_node"),
                1
            ), "activate_level_41_talent_node_once")
        end
    )
    for level = 43, 53 do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] =
            retarget_shifted_talent_plan(original_talent_by_level[level - 1], level, level - 1)
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[54] = make_composite_talent_plan(
        54,
        "54级天赋：补位激活53级节点并激活54级节点一次",
        original_talent_by_level[54],
        function(steps, level)
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[53], "select_level_53_talent_node"), "select_level_53_talent_node")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[53], "activate_level_53_talent_node"), "activate_level_53_talent_node")
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[54], "select_level_54_talent_node"), "select_level_54_talent_node")
            append_shifted_step(steps, level, clone_activation_step_for_repeat(
                find_step_by_key_contains(original_talent_by_level[54], "activate_level_54_talent_node"),
                1
            ), "activate_level_54_talent_node_once")
        end
    )

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[55] = make_composite_talent_plan(
        55,
        "55级天赋：补位激活54级节点第二次",
        original_talent_by_level[54],
        function(steps, level)
            append_shifted_step(steps, level, find_step_by_key_contains(original_talent_by_level[54], "select_level_54_talent_node"), "select_level_54_talent_node")
            append_shifted_step(steps, level, clone_activation_step_for_repeat(
                find_step_by_key_contains(original_talent_by_level[54], "activate_level_54_talent_node"),
                1
            ), "activate_level_54_talent_node_once")
        end
    )
    for level = 56, 59 do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] =
            retarget_shifted_talent_plan(original_talent_by_level[level - 1], level, level - 1)
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[60] = nil

    -- Level 18 has a newly sampled node. Insert it into the already-corrected
    -- sequence, then let level 19+ consume the previous level's corrected plan.
    local pre_level_18_insert_talent_by_level = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level)

    local function make_inserted_level_18_talent_plan()
        return make_manual_talent_plan(
            18,
            "18级天赋：激活新增天赋节点",
            function(steps, level)
                local base_plan = pre_level_18_insert_talent_by_level[18] or original_talent_by_level[18]
                append_shifted_setup(steps, level, base_plan)
                append_shifted_step(steps, level, maintenance_locator_step(
                    "select_level_18_inserted_talent_node",
                    "18级天赋：选择新增天赋节点",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    913.706848,
                    478.651550,
                    0.634960,
                    0.531835,
                    20,
                    1200
                ), "select_level_18_inserted_talent_node")
                append_shifted_step(steps, level, maintenance_locator_step(
                    "activate_level_18_inserted_talent_node",
                    "18级天赋：激活新增天赋节点",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    743.509155,
                    673.801453,
                    0.516685,
                    0.748668,
                    80,
                    650
                ), "activate_level_18_inserted_talent_node")
                append_shifted_back(steps, level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[18] = make_inserted_level_18_talent_plan()
    for level = 19, 60 do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] =
            retarget_shifted_talent_plan(pre_level_18_insert_talent_by_level[level - 1], level, level - 1)
    end

    local function append_level_19_extra_fixed_talent_steps(steps, level, base_plan)
        append_shifted_step(
            steps,
            level,
            find_step_by_key_contains(base_plan, "open_fast_entrance_menu"),
            "level_19_extra_open_fast_entrance_menu"
        )
        append_shifted_step(
            steps,
            level,
            find_step_by_key_contains(base_plan, "open_talent_panel"),
            "level_19_extra_open_talent_panel"
        )
        append_shifted_step(steps, level, extra_talent_fixed_click_step(
            "level_19_extra_talent_tab_click",
            "19级额外天赋：点击额外页签",
            492.00,
            247.00,
            0.341904,
            0.274444,
            650
        ), "level_19_extra_talent_tab_click")
        append_shifted_step(steps, level, extra_talent_fixed_click_step(
            "level_19_extra_talent_node_click",
            "19级额外天赋：点击额外节点",
            721.00,
            614.00,
            0.501042,
            0.682222,
            900
        ), "level_19_extra_talent_node_click")
        append_shifted_step(steps, level, extra_talent_fixed_click_step(
            "level_19_extra_talent_confirm_click",
            "19级额外天赋：点击额外确认",
            1227.00,
            207.00,
            0.852675,
            0.230000,
            650
        ), "level_19_extra_talent_confirm_click")
        append_shifted_step(
            steps,
            level,
            find_step_by_key_contains(base_plan, "back_from_talent"),
            "level_19_extra_back_from_talent_panel"
        )
    end

    local function make_level_19_extra_fixed_talent_plan()
        return {
            key = "level_19_talent_extra_fixed_clicks",
            label = "19级额外天赋：固定鼠标点击",
            require_available_points = false,
            close_with_escape = false,
            steps = {}
        }
    end

    local function make_level_19_manual_talent_plan()
        return make_manual_talent_plan(
            19,
            "19级天赋：激活指定天赋节点三次",
            function(steps, level)
                local base_plan = pre_level_18_insert_talent_by_level[19]
                    or pre_level_18_insert_talent_by_level[18]
                    or original_talent_by_level[19]
                append_shifted_setup(steps, level, base_plan)
                append_shifted_step(steps, level, maintenance_locator_step(
                    "select_level_19_manual_talent_node",
                    "19级天赋：选择指定天赋节点",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    923.706848,
                    411.208282,
                    0.641909,
                    0.456898,
                    20,
                    1200
                ), "select_level_19_manual_talent_node")

                local activate_step = maintenance_locator_step(
                    "activate_level_19_manual_talent_node_triple",
                    "19级天赋：激活指定天赋节点三次",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                    },
                    753.509155,
                    675.801453,
                    0.523634,
                    0.750891,
                    80,
                    650
                )
                activate_step.click_repeat_count = 3
                activate_step.click_repeat_interval_ms = 180
                append_shifted_step(steps, level, activate_step, "activate_level_19_manual_talent_node_triple")
                append_shifted_back(steps, level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[19] = make_level_19_manual_talent_plan()

    do
        local extra_plan = make_level_19_extra_fixed_talent_plan()
        local extra_base_plan = pre_level_18_insert_talent_by_level[19]
            or pre_level_18_insert_talent_by_level[18]
            or original_talent_by_level[19]
        append_level_19_extra_fixed_talent_steps(extra_plan.steps, 19, extra_base_plan)
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_extra_by_level[19] = extra_plan
    end

    local function make_level_20_to_22_manual_talent_plan(level)
        return make_manual_talent_plan(
            level,
            string.format("%d级天赋：激活指定天赋节点", level),
            function(steps, current_level)
                local base_plan = pre_level_18_insert_talent_by_level[current_level]
                    or pre_level_18_insert_talent_by_level[19]
                    or pre_level_18_insert_talent_by_level[18]
                    or original_talent_by_level[current_level]
                append_shifted_setup(steps, current_level, base_plan)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("select_level_%d_manual_talent_node", current_level),
                    string.format("%d级天赋：选择指定天赋节点", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1079.636841,
                    411.208282,
                    0.750269,
                    0.456898,
                    20,
                    1200
                ), string.format("select_level_%d_manual_talent_node", current_level))

                local activate_step = maintenance_locator_step(
                    string.format("activate_level_%d_manual_talent_node", current_level),
                    string.format("%d级天赋：激活指定天赋节点", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    909.439148,
                    675.801453,
                    0.631994,
                    0.750891,
                    80,
                    650
                )
                activate_step.exclude_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                }
                append_shifted_step(
                    steps,
                    current_level,
                    activate_step,
                    string.format("activate_level_%d_manual_talent_node", current_level)
                )
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    for _, level in ipairs({ 20, 21, 22 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_20_to_22_manual_talent_plan(level)
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[61] = nil

    -- The talent route is being resampled from scratch. Keep the currently
    -- verified early levels only; later legacy shift/patch plans must not run.
    for level = 15, 61 do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = nil
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_extra_by_level[level] = nil
    end

    local function make_level_15_to_17_resampled_talent_plan(level)
        return make_manual_talent_plan(
            level,
            string.format("%d级天赋：激活同组天赋节点", level),
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("select_level_%d_resampled_talent_node", current_level),
                    string.format("%d级天赋节点按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    919.706848,
                    479.651550,
                    0.639129,
                    0.532946,
                    20,
                    1200
                ), string.format("select_level_%d_resampled_talent_node", current_level))
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("activate_level_%d_resampled_talent_node", current_level),
                    string.format("%d级天赋节点激活按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    749.509155,
                    674.801453,
                    0.520854,
                    0.749779,
                    80,
                    650
                ), string.format("activate_level_%d_resampled_talent_node", current_level))
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    for _, level in ipairs({ 15, 16, 17 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_15_to_17_resampled_talent_plan(level)
    end

    local function make_level_18_to_20_resampled_talent_plan(level)
        return make_manual_talent_plan(
            level,
            string.format("%d级天赋：激活同组天赋节点", level),
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("select_level_%d_resampled_talent_node", current_level),
                    string.format("%d级天赋节点按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    913.706848,
                    407.208282,
                    0.634960,
                    0.452454,
                    20,
                    1200
                ), string.format("select_level_%d_resampled_talent_node", current_level))
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("activate_level_%d_resampled_talent_node", current_level),
                    string.format("%d级天赋节点激活按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    743.509155,
                    671.801453,
                    0.516685,
                    0.746446,
                    80,
                    650
                ), string.format("activate_level_%d_resampled_talent_node", current_level))
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    for _, level in ipairs({ 18, 19, 20 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_18_to_20_resampled_talent_plan(level)
    end

    local function make_level_21_to_23_resampled_talent_plan(level)
        return make_manual_talent_plan(
            level,
            string.format("%d级天赋：激活同组天赋节点", level),
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("select_level_%d_resampled_talent_node", current_level),
                    string.format("%d级天赋节点按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1069.636841,
                    407.208282,
                    0.743320,
                    0.452454,
                    20,
                    1200
                ), string.format("select_level_%d_resampled_talent_node", current_level))
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("activate_level_%d_resampled_talent_node", current_level),
                    string.format("%d级天赋节点激活按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    899.439148,
                    671.801453,
                    0.625045,
                    0.746446,
                    80,
                    650
                ), string.format("activate_level_%d_resampled_talent_node", current_level))
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    for _, level in ipairs({ 21, 22, 23 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_21_to_23_resampled_talent_plan(level)
    end

    local function make_level_24_resampled_talent_plan()
        return make_manual_talent_plan(
            24,
            "24级天赋：激活同组天赋节点并执行额外固定点击",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "select_level_24_resampled_talent_node",
                    "24级天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1225.566772,
                    407.208282,
                    0.851679,
                    0.452454,
                    20,
                    1200
                ), "select_level_24_resampled_talent_node")
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "activate_level_24_resampled_talent_node",
                    "24级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    1055.369263,
                    671.801453,
                    0.733405,
                    0.746446,
                    80,
                    650
                ), "activate_level_24_resampled_talent_node")
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_24_extra_talent_tab_click",
                    "24按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.KeyStoneItem2.WidgetTree.TabBtn"
                    },
                    461.451080,
                    251.016342,
                    0.320675,
                    0.278907,
                    80,
                    650
                ), "level_24_extra_talent_tab_click")
                steps[#steps].distance_anchor_exact_text = "24"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.KeyStoneItem2.WidgetTree.TabBtn"
                steps[#steps].distance_min = 50.743154
                steps[#steps].distance_max = 53.881905
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_24_extra_talent_node_click",
                    "2按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabKeyStoneItem_C.WidgetTree.SelectBtn2"
                    },
                    646.869873,
                    634.373291,
                    0.449527,
                    0.704859,
                    80,
                    900
                ), "level_24_extra_talent_node_click")
                steps[#steps].distance_anchor_exact_text = "2"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TabKeyStoneItem_C.WidgetTree.SelectBtn2"
                steps[#steps].distance_min = 31.585863
                steps[#steps].distance_max = 33.539628
                append_shifted_step(steps, current_level, extra_talent_fixed_click_step(
                    "level_24_extra_talent_confirm_click",
                    "24级额外天赋：点击额外确认",
                    1203.00,
                    159.00,
                    0.835997,
                    0.176667,
                    650
                ), "level_24_extra_talent_confirm_click")
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[24] = make_level_24_resampled_talent_plan()

    local function make_level_25_second_tab_talent_plan()
        return make_manual_talent_plan(
            25,
            "25级天赋：激活第二页天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_25_select_second_talent_tab",
                    "123按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                    },
                    49.726845,
                    391.616241,
                    0.034557,
                    0.435129,
                    80,
                    650
                ), "level_25_select_second_talent_tab")
                steps[#steps].distance_anchor_exact_text = "123"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                steps[#steps].distance_min = 67.184104
                steps[#steps].distance_max = 71.339822
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_25_select_talent_category",
                    "25级天赋大类按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.UICareerPointItem_C.WidgetTree.SelectBtn"
                    },
                    1044.438477,
                    468.168762,
                    0.725809,
                    0.520188,
                    80,
                    650
                ), "level_25_select_talent_category")
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_25_activate_talent_category",
                    "25级天赋大类激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TipCareerItem_C.WidgetTree.ActiveBtn"
                    },
                    1245.157104,
                    472.037933,
                    0.865293,
                    0.524487,
                    80,
                    650
                ), "level_25_activate_talent_category")
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_25_select_talent_node",
                    "25级天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    292.986755,
                    407.208282,
                    0.203604,
                    0.452454,
                    20,
                    1200
                ), "level_25_select_talent_node")
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_25_activate_talent_node",
                    "25级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    503.438232,
                    671.801453,
                    0.349853,
                    0.746446,
                    80,
                    650
                ), "level_25_activate_talent_node")
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[25] = make_level_25_second_tab_talent_plan()

    local function append_level_26_to_29_second_tab_step(steps, current_level)
        append_shifted_step(steps, current_level, maintenance_locator_step(
            string.format("level_%d_select_second_talent_tab", current_level),
            "123按钮",
            {
                "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
            },
            48.726845,
            392.616241,
            0.033862,
            0.436240,
            80,
            650
        ), string.format("level_%d_select_second_talent_tab", current_level))
        steps[#steps].distance_anchor_exact_text = "123"
        steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
        steps[#steps].distance_min = 67.184104
        steps[#steps].distance_max = 71.339822
    end

    local function make_level_26_second_tab_talent_plan()
        return make_manual_talent_plan(
            26,
            "26级天赋：激活第二页天赋节点两次",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                append_level_26_to_29_second_tab_step(steps, current_level)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_26_select_second_tab_talent_node",
                    "26级天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    313.183411,
                    412.652100,
                    0.217488,
                    0.457993,
                    30,
                    1200
                ), "level_26_select_second_tab_talent_node")
                local activate_step = maintenance_locator_step(
                    "level_26_activate_second_tab_talent_node_twice",
                    "26级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    502.438232,
                    672.801453,
                    0.349158,
                    0.747557,
                    80,
                    650
                )
                activate_step.click_repeat_count = 2
                activate_step.click_repeat_interval_ms = 180
                append_shifted_step(steps, current_level, activate_step, "level_26_activate_second_tab_talent_node_twice")
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    local function make_level_27_to_29_second_tab_talent_plan(level)
        return make_manual_talent_plan(
            level,
            string.format("%d级天赋：激活第二页同组天赋节点", level),
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                local node_hint = {
                    x = 444.221771,
                    y = 406.152130,
                    ratio_x = 0.308487,
                    ratio_y = 0.451280,
                }
                append_shifted_setup(steps, current_level, base_plan)
                append_level_26_to_29_second_tab_step(steps, current_level)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_second_tab_talent_node", current_level),
                    string.format("%d级天赋节点按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    node_hint.x,
                    node_hint.y,
                    node_hint.ratio_x,
                    node_hint.ratio_y,
                    30,
                    1200
                ), string.format("level_%d_select_second_tab_talent_node", current_level))
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_activate_second_tab_talent_node", current_level),
                    string.format("%d级天赋节点激活按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    658.368225,
                    672.801453,
                    0.457518,
                    0.747557,
                    80,
                    650
                ), string.format("level_%d_activate_second_tab_talent_node", current_level))
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[26] = make_level_26_second_tab_talent_plan()
    for _, level in ipairs({ 27, 28, 29 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_27_to_29_second_tab_talent_plan(level)
    end

    local function append_level_30_to_32_second_tab_step(steps, current_level)
        append_shifted_step(steps, current_level, maintenance_locator_step(
            string.format("level_%d_select_second_talent_tab", current_level),
            "123按钮",
            {
                "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
            },
            48.726845,
            399.616241,
            0.033862,
            0.444018,
            80,
            650
        ), string.format("level_%d_select_second_talent_tab", current_level))
        steps[#steps].distance_anchor_exact_text = "123"
        steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
        steps[#steps].distance_min = 67.184104
        steps[#steps].distance_max = 71.339822
    end

    local function make_level_30_to_32_second_tab_talent_plan(level)
        return make_manual_talent_plan(
            level,
            string.format("%d级天赋：激活第二页下排同组天赋节点", level),
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                append_level_30_to_32_second_tab_step(steps, current_level)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_second_tab_talent_node", current_level),
                    string.format("%d级天赋节点按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    469.221771,
                    621.126648,
                    0.325848,
                    0.689375,
                    20,
                    1200
                ), string.format("level_%d_select_second_tab_talent_node", current_level))
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_activate_second_tab_talent_node", current_level),
                    string.format("%d级天赋节点激活按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    658.368225,
                    679.801453,
                    0.457518,
                    0.755335,
                    80,
                    650
                ), string.format("level_%d_activate_second_tab_talent_node", current_level))
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    for _, level in ipairs({ 30, 31, 32 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_30_to_32_second_tab_talent_plan(level)
    end

    local function append_level_33_to_34_second_tab_step(steps, current_level)
        append_shifted_step(steps, current_level, maintenance_locator_step(
            string.format("level_%d_select_second_talent_tab", current_level),
            "123按钮",
            {
                "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
            },
            46.726845,
            391.616241,
            0.032472,
            0.435129,
            80,
            650
        ), string.format("level_%d_select_second_talent_tab", current_level))
        steps[#steps].distance_anchor_exact_text = "123"
        steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
        steps[#steps].distance_min = 67.184104
        steps[#steps].distance_max = 71.339822
    end

    local function make_level_33_to_34_second_tab_talent_plan(level, activate_repeat_count)
        return make_manual_talent_plan(
            level,
            string.format("%d级天赋：激活第二页中排天赋节点", level),
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                append_level_33_to_34_second_tab_step(steps, current_level)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_second_tab_talent_node", current_level),
                    string.format("%d级天赋节点按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    781.298584,
                    482.143616,
                    0.542568,
                    0.535121,
                    20,
                    1200
                ), string.format("level_%d_select_second_tab_talent_node", current_level))
                local activate_step = maintenance_locator_step(
                    string.format("level_%d_activate_second_tab_talent_node", current_level),
                    string.format("%d级天赋节点激活按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    587.579102,
                    671.801453,
                    0.408325,
                    0.746446,
                    80,
                    650
                )
                if tonumber(activate_repeat_count) ~= nil and tonumber(activate_repeat_count) > 1 then
                    activate_step.click_repeat_count = tonumber(activate_repeat_count)
                    activate_step.click_repeat_interval_ms = 180
                end
                append_shifted_step(steps, current_level, activate_step, string.format("level_%d_activate_second_tab_talent_node", current_level))
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[33] = make_level_33_to_34_second_tab_talent_plan(33, 2)
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[34] = make_level_33_to_34_second_tab_talent_plan(34, 1)

    local function make_level_35_second_tab_talent_plan()
        return make_manual_talent_plan(
            35,
            "35级天赋：激活第二页右侧中排天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                append_level_33_to_34_second_tab_step(steps, current_level)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_35_select_second_tab_talent_node",
                    "35级天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    915.336914,
                    476.143616,
                    0.635651,
                    0.528461,
                    30,
                    1200
                ), "level_35_select_second_tab_talent_node")
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_35_activate_second_tab_talent_node",
                    "35级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    743.509155,
                    671.801453,
                    0.516685,
                    0.746446,
                    80,
                    650
                ), "level_35_activate_second_tab_talent_node")
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    local function make_level_36_to_38_second_tab_talent_plan(level)
        return make_manual_talent_plan(
            level,
            string.format("%d级天赋：激活第二页右侧上排天赋节点", level),
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                append_level_33_to_34_second_tab_step(steps, current_level)
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_second_tab_talent_node", current_level),
                    string.format("%d级天赋节点按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    915.336914,
                    406.652100,
                    0.635651,
                    0.451334,
                    30,
                    1200
                ), string.format("level_%d_select_second_tab_talent_node", current_level))
                steps[#steps].target_poll_count = 30
                steps[#steps].target_poll_interval_ms = 100
                steps[#steps].fixed_fallback_client_x = 958.00
                steps[#steps].fixed_fallback_client_y = 415.00
                steps[#steps].fixed_fallback_ratio_x = 0.665278
                steps[#steps].fixed_fallback_ratio_y = 0.460599
                steps[#steps].fixed_fallback_prefer_ratio = true
                steps[#steps].fixed_fallback_mouse_mode = "api"
                steps[#steps].fixed_fallback_click_delay_ms = 50
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_activate_second_tab_talent_node", current_level),
                    string.format("%d级天赋节点激活按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    743.509155,
                    671.801453,
                    0.516685,
                    0.746446,
                    80,
                    650
                ), string.format("level_%d_activate_second_tab_talent_node", current_level))
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[35] = make_level_35_second_tab_talent_plan()
    for _, level in ipairs({ 36, 37, 38 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_36_to_38_second_tab_talent_plan(level)
    end

    local function make_level_39_to_41_second_tab_talent_plan(level, node_hint)
        node_hint = type(node_hint) == "table" and node_hint or {}
        local second_tab_hint = type(node_hint.second_tab) == "table" and node_hint.second_tab or nil
        return make_manual_talent_plan(
            level,
            string.format("%d级天赋：激活第二页右侧上排后续天赋节点", level),
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)
                if second_tab_hint ~= nil then
                    append_shifted_step(steps, current_level, maintenance_locator_step(
                        string.format("level_%d_select_second_talent_tab", current_level),
                        "123按钮",
                        {
                            "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                        },
                        second_tab_hint.x or 45.754456,
                        second_tab_hint.y or 389.549255,
                        second_tab_hint.ratio_x or 0.031774,
                        second_tab_hint.ratio_y or 0.432833,
                        second_tab_hint.max_distance or 30,
                        650
                    ), string.format("level_%d_select_second_talent_tab", current_level))
                    steps[#steps].distance_anchor_exact_text = "123"
                    steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                    steps[#steps].distance_min = second_tab_hint.distance_min or 67.231408
                    steps[#steps].distance_max = second_tab_hint.distance_max or 71.390052
                end
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_second_tab_talent_node", current_level),
                    string.format("%d级天赋节点按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    node_hint.x or 1070.636841,
                    node_hint.y or 406.208282,
                    node_hint.ratio_x or 0.744014,
                    node_hint.ratio_y or 0.451343,
                    node_hint.max_distance or 20,
                    1200
                ), string.format("level_%d_select_second_tab_talent_node", current_level))
                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_activate_second_tab_talent_node", current_level),
                    string.format("%d级天赋节点激活按钮", current_level),
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                    },
                    900.439148,
                    670.801453,
                    0.625740,
                    0.745335,
                    80,
                    650
                ), string.format("level_%d_activate_second_tab_talent_node", current_level))
                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[39] = make_level_39_to_41_second_tab_talent_plan(39, {
        x = 1071.375366,
        y = 406.652100,
        ratio_x = 0.744011,
        ratio_y = 0.451334,
        max_distance = 30
    })
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[40] = make_level_39_to_41_second_tab_talent_plan(40)
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[41] = make_level_39_to_41_second_tab_talent_plan(41, {
        second_tab = {
            x = 45.754456,
            y = 389.549255,
            ratio_x = 0.031774,
            ratio_y = 0.432833,
            max_distance = 30,
            distance_min = 67.231408,
            distance_max = 71.390052
        }
    })

    local function make_level_42_second_tab_talent_plan()
        return make_manual_talent_plan(
            42,
            "42级天赋：激活第二页右上天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_42_select_second_talent_tab",
                    "123按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                    },
                    42.726845,
                    388.616241,
                    0.029692,
                    0.431796,
                    80,
                    650
                ), "level_42_select_second_talent_tab")
                steps[#steps].distance_anchor_exact_text = "123"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                steps[#steps].distance_min = 67.184104
                steps[#steps].distance_max = 71.339822

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_42_select_second_tab_talent_node",
                    "42级天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1221.566772,
                    404.208282,
                    0.848900,
                    0.449120,
                    20,
                    1200
                ), "level_42_select_second_tab_talent_node")

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_42_activate_second_tab_talent_node",
                    "42级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    1051.369263,
                    668.801453,
                    0.730625,
                    0.743113,
                    80,
                    650
                ), "level_42_activate_second_tab_talent_node")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[42] = make_level_42_second_tab_talent_plan()

    local function make_level_43_second_tab_talent_plan()
        return make_manual_talent_plan(
            43,
            "43级天赋：激活第二页下排天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_43_select_second_talent_tab",
                    "123按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                    },
                    42.726845,
                    388.616241,
                    0.029692,
                    0.431796,
                    80,
                    650
                ), "level_43_select_second_talent_tab")
                steps[#steps].distance_anchor_exact_text = "123"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                steps[#steps].distance_min = 67.184104
                steps[#steps].distance_max = 71.339822

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_43_select_second_tab_talent_node",
                    "43级天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1073.375366,
                    616.626709,
                    0.745400,
                    0.685141,
                    20,
                    1200
                ), "level_43_select_second_tab_talent_node")
                apply_locator_fixed_fallback(steps[#steps], 1073.375366, 616.626709, 0.745400, 0.685141)

                local activate_step = maintenance_locator_step(
                    "level_43_activate_second_tab_talent_node_twice",
                    "43级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    895.439148,
                    668.801453,
                    0.622265,
                    0.743113,
                    80,
                    650
                )
                activate_step.click_repeat_count = 2
                activate_step.click_repeat_interval_ms = 180
                append_shifted_step(steps, current_level, activate_step, "level_43_activate_second_tab_talent_node_twice")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[43] = make_level_43_second_tab_talent_plan()

    local function make_level_44_second_tab_talent_plan()
        return make_manual_talent_plan(
            44,
            "44级天赋：激活第二页下排天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_44_select_second_talent_tab",
                    "123按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                    },
                    42.726845,
                    388.616241,
                    0.029692,
                    0.431796,
                    80,
                    650
                ), "level_44_select_second_talent_tab")
                steps[#steps].distance_anchor_exact_text = "123"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                steps[#steps].distance_min = 67.184104
                steps[#steps].distance_max = 71.339822

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_44_select_second_tab_talent_node",
                    "44级天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1073.375366,
                    616.626709,
                    0.745400,
                    0.685141,
                    20,
                    1200
                ), "level_44_select_second_tab_talent_node")
                apply_locator_fixed_fallback(steps[#steps], 1073.375366, 616.626709, 0.745400, 0.685141)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_44_activate_second_tab_talent_node",
                    "44级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    895.439148,
                    668.801453,
                    0.622265,
                    0.743113,
                    80,
                    650
                ), "level_44_activate_second_tab_talent_node")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[44] = make_level_44_second_tab_talent_plan()

    local function make_level_45_second_tab_talent_plan()
        return make_manual_talent_plan(
            45,
            "45级天赋：激活第二页右下天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_45_select_second_talent_tab",
                    "123按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                    },
                    42.726845,
                    388.616241,
                    0.029692,
                    0.431796,
                    80,
                    1200
                ), "level_45_select_second_talent_tab")
                steps[#steps].distance_anchor_exact_text = "123"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                steps[#steps].distance_min = 67.184104
                steps[#steps].distance_max = 71.339822

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_45_select_second_tab_talent_node",
                    "45级天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1221.566772,
                    612.538025,
                    0.848900,
                    0.680598,
                    20,
                    1200
                ), "level_45_select_second_tab_talent_node")

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_45_activate_second_tab_talent_node",
                    "45级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    1051.369263,
                    668.801453,
                    0.730625,
                    0.743113,
                    80,
                    650
                ), "level_45_activate_second_tab_talent_node")

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_45_select_keystone_24_tab",
                    "24按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.KeyStoneItem1.WidgetTree.TabBtn"
                    },
                    358.077972,
                    252.016342,
                    0.248838,
                    0.280018,
                    80,
                    650
                ), "level_45_select_keystone_24_tab")
                steps[#steps].distance_anchor_exact_text = "24"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.KeyStoneItem1.WidgetTree.TabBtn"
                steps[#steps].distance_min = 50.743154
                steps[#steps].distance_max = 53.881905

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_45_select_keystone_choice",
                    "45级基石选择按钮4",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabKeyStoneItem_C.WidgetTree.SelectBtn4"
                    },
                    1042.125000,
                    633.474976,
                    0.723698,
                    0.703861,
                    30,
                    650
                ), "level_45_select_keystone_choice")

                append_shifted_step(steps, current_level, make_maintenance_fixed_click_step({
                    key = "level_45_talent_blank_click_after_keystone_choice",
                    label = "45级天赋：选择后空白点击",
                    fixed_client_x = 720,
                    fixed_client_y = 450,
                    fixed_ratio_x = 0.500347,
                    fixed_ratio_y = 0.500000,
                    wait_after_ms = 650
                }), "level_45_talent_blank_click_after_keystone_choice")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[45] = make_level_45_second_tab_talent_plan()

    local function make_level_46_to_48_second_tab_talent_plan(level)
        return make_manual_talent_plan(
            level,
            tostring(level) .. "级天赋：激活第二页中排天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_second_talent_tab", current_level),
                    "123按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                    },
                    47.726845,
                    392.616241,
                    0.033167,
                    0.436240,
                    80,
                    1200
                ), "select_second_talent_tab")
                steps[#steps].distance_anchor_exact_text = "123"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                steps[#steps].distance_min = 67.184104
                steps[#steps].distance_max = 71.339822

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_second_tab_talent_node", current_level),
                    tostring(current_level) .. "级天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    914.706848,
                    547.094788,
                    0.635655,
                    0.607883,
                    20,
                    1200
                ), "select_second_tab_talent_node")

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_activate_second_tab_talent_node", current_level),
                    tostring(current_level) .. "级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    744.509155,
                    672.801453,
                    0.517380,
                    0.747557,
                    80,
                    650
                ), "activate_second_tab_talent_node")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    for level = 46, 48 do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_46_to_48_second_tab_talent_plan(level)
    end

    local function make_level_49_second_tab_talent_plan()
        return make_manual_talent_plan(
            49,
            "49级天赋：激活第二页中排后续天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_49_select_second_talent_tab",
                    "123按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                    },
                    47.726845,
                    392.616241,
                    0.033167,
                    0.436240,
                    80,
                    1200
                ), "level_49_select_second_talent_tab")
                steps[#steps].distance_anchor_exact_text = "123"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
                steps[#steps].distance_min = 67.184104
                steps[#steps].distance_max = 71.339822

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_49_select_second_tab_talent_node",
                    "49级天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1070.636841,
                    547.094788,
                    0.744014,
                    0.607883,
                    20,
                    1200
                ), "level_49_select_second_tab_talent_node")

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_49_activate_second_tab_talent_node",
                    "49级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn"
                    },
                    900.439148,
                    672.801453,
                    0.625740,
                    0.747557,
                    80,
                    650
                ), "level_49_activate_second_tab_talent_node")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[49] = make_level_49_second_tab_talent_plan()

    local function make_level_50_to_52_trickster_talent_plan(level)
        return make_manual_talent_plan(
            level,
            tostring(level) .. "级天赋：激活欺诈之神中排天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_trickster_god_tab", current_level),
                    "欺诈之神按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
                    },
                    47.726845,
                    337.195770,
                    0.033167,
                    0.374662,
                    80,
                    1200
                ), "select_trickster_god_tab")
                steps[#steps].distance_anchor_exact_text = "欺诈之神"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
                steps[#steps].distance_min = 68.595838
                steps[#steps].distance_max = 72.838879

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_trickster_talent_node", current_level),
                    "天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    602.84680175781,
                    408.208282,
                    0.418935,
                    0.453565,
                    20,
                    1200
                ), "select_trickster_talent_node")

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_activate_trickster_talent_node", current_level),
                    tostring(current_level) .. "级天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                    },
                    813.298279,
                    672.801453,
                    0.565183,
                    0.747557,
                    80,
                    650
                ), "activate_trickster_talent_node")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    for level = 50, 52 do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_50_to_52_trickster_talent_plan(level)
    end

    local function make_level_53_trickster_talent_plan()
        return make_manual_talent_plan(
            53,
            "53级天赋：激活欺诈之神上排后续天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_53_select_trickster_god_tab",
                    "欺诈之神按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
                    },
                    47.726845,
                    337.195770,
                    0.033167,
                    0.374662,
                    80,
                    1200
                ), "level_53_select_trickster_god_tab")
                steps[#steps].distance_anchor_exact_text = "欺诈之神"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
                steps[#steps].distance_min = 68.595838
                steps[#steps].distance_max = 72.838879

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_53_select_trickster_talent_node",
                    "天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    758.776794,
                    408.208282,
                    0.527295,
                    0.453565,
                    20,
                    1200
                ), "level_53_select_trickster_talent_node")

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_53_activate_trickster_talent_node",
                    "天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                    },
                    588.579102,
                    672.801453,
                    0.409020,
                    0.747557,
                    80,
                    650
                ), "level_53_activate_trickster_talent_node")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[53] = make_level_53_trickster_talent_plan()

    local function make_level_54_to_56_trickster_talent_plan(level)
        return make_manual_talent_plan(
            level,
            tostring(level) .. "级天赋：激活欺诈之神底排天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_trickster_god_tab", current_level),
                    "欺诈之神按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
                    },
                    47.726845,
                    337.195770,
                    0.033167,
                    0.374662,
                    80,
                    1200
                ), "select_trickster_god_tab")
                steps[#steps].distance_anchor_exact_text = "欺诈之神"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
                steps[#steps].distance_min = 68.595838
                steps[#steps].distance_max = 72.838879

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_trickster_bottom_talent_node", current_level),
                    "天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1070.636841,
                    685.981323,
                    0.744014,
                    0.762201,
                    20,
                    1200
                ), "select_trickster_bottom_talent_node")

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_activate_trickster_bottom_talent_node", current_level),
                    "天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                    },
                    900.439148,
                    672.801453,
                    0.625740,
                    0.747557,
                    80,
                    650
                ), "activate_trickster_bottom_talent_node")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    for level = 54, 56 do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_54_to_56_trickster_talent_plan(level)
    end

    local function make_level_57_trickster_talent_plan()
        return make_manual_talent_plan(
            57,
            "57级天赋：激活欺诈之神右下天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_57_select_trickster_god_tab",
                    "欺诈之神按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
                    },
                    47.726845,
                    337.195770,
                    0.033167,
                    0.374662,
                    80,
                    1200
                ), "level_57_select_trickster_god_tab")
                steps[#steps].distance_anchor_exact_text = "欺诈之神"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
                steps[#steps].distance_min = 68.595838
                steps[#steps].distance_max = 72.838879

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_57_select_trickster_talent_node",
                    "天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1226.566772,
                    685.981323,
                    0.852374,
                    0.762201,
                    20,
                    1200
                ), "level_57_select_trickster_talent_node")

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    "level_57_activate_trickster_talent_node",
                    "天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                    },
                    1056.369263,
                    672.801453,
                    0.734100,
                    0.747557,
                    80,
                    650
                ), "level_57_activate_trickster_talent_node")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[57] = make_level_57_trickster_talent_plan()

    local function make_level_58_to_60_trickster_talent_plan(level)
        return make_manual_talent_plan(
            level,
            tostring(level) .. "级天赋：激活欺诈之神右侧天赋节点",
            function(steps, current_level)
                local base_plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[14]
                    or original_talent_by_level[14]
                append_shifted_setup(steps, current_level, base_plan)

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_trickster_god_tab", current_level),
                    "欺诈之神按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
                    },
                    47.726845,
                    337.195770,
                    0.033167,
                    0.374662,
                    80,
                    1200
                ), "select_trickster_god_tab")
                steps[#steps].distance_anchor_exact_text = "欺诈之神"
                steps[#steps].distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
                steps[#steps].distance_min = 68.595838
                steps[#steps].distance_max = 72.838879

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_select_trickster_right_talent_node", current_level),
                    "天赋节点按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                    },
                    1070.636841,
                    477.651550,
                    0.744014,
                    0.530724,
                    20,
                    1200
                ), "select_trickster_right_talent_node")

                append_shifted_step(steps, current_level, maintenance_locator_step(
                    string.format("level_%d_activate_trickster_right_talent_node", current_level),
                    "天赋节点激活按钮",
                    {
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtn",
                        "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.TipTalentItem.WidgetTree.ActiveBtnGray"
                    },
                    900.439148,
                    672.801453,
                    0.625740,
                    0.747557,
                    80,
                    650
                ), "activate_trickster_right_talent_node")

                append_shifted_back(steps, current_level, base_plan)
            end
        )
    end

    for level = 58, 60 do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] = make_level_58_to_60_trickster_talent_plan(level)
    end
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[59] = nil
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[60] = nil

    local function make_select_trickster_god_tab_step(level)
        return make_maintenance_locator_step({
            key = string.format("level_%d_select_trickster_god_tab", tonumber(level) or 0),
            label = "欺诈之神按钮",
            distance_anchor_exact_text = "欺诈之神",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn",
            distance_min = 68.595838,
            distance_max = 72.838879,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem1.WidgetTree.TabBtn"
            },
            hint_client_x = 49.726845,
            hint_client_y = 336.195770,
            hint_ratio_x = 0.034557,
            hint_ratio_y = 0.373551,
            hint_max_distance = 80,
            wait_after_ms = 650
        })
    end

    local function plan_has_step_key_contains(plan, needle)
        local steps = type(plan) == "table" and type(plan.steps) == "table" and plan.steps or {}
        for _, step in ipairs(steps) do
            if tostring(step.key or ""):find(tostring(needle or ""), 1, true) ~= nil then
                return true
            end
        end
        return false
    end

    local function insert_trickster_god_tab_after_talent_open(level)
        local plan = M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level]
        local steps = type(plan) == "table" and type(plan.steps) == "table" and plan.steps or nil
        if steps == nil or plan_has_step_key_contains(plan, "select_trickster_god_tab") then
            return
        end
        for index, step in ipairs(steps) do
            if tostring(step.key or ""):find("open_talent_panel", 1, true) ~= nil then
                table.insert(steps, index + 1, make_select_trickster_god_tab_step(level))
                return
            end
        end
    end

    for level = 1, 24 do
        insert_trickster_god_tab_after_talent_open(level)
    end

    local function is_second_talent_tab_step(step)
        if type(step) ~= "table" then
            return false
        end
        local needle = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.TabCareerSelectItem.WidgetTree.TabCardItem2.WidgetTree.TabBtn"
        if tostring(step.distance_button_name or "") == needle then
            return true
        end
        for _, pattern in ipairs(type(step.include_patterns) == "table" and step.include_patterns or {}) do
            if tostring(pattern or "") == needle then
                return true
            end
        end
        return false
    end

    for _, plan in pairs(type(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level) == "table" and M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level or {}) do
        for _, step in ipairs(type(plan) == "table" and type(plan.steps) == "table" and plan.steps or {}) do
            if is_second_talent_tab_step(step) then
                step.wait_after_ms = math.max(1200, tonumber(step.wait_after_ms) or 0)
            end
        end
    end
end

do
    local level_4_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_4_skill_plan.key = "level_4_skill_upgrade_range_sequence"
    level_4_skill_plan.label = "4级技能：找图升级并配置范围扩大"
    level_4_skill_plan.close_with_escape = false
    mark_skill_pre_add_bag_cleanup(level_4_skill_plan)

    local steps = type(level_4_skill_plan.steps) == "table" and level_4_skill_plan.steps or {}
    if #steps > 0 and tostring(steps[#steps].key or "") == "back_from_skill_panel" then
        table.remove(steps, #steps)
    end
    for _, step in ipairs(steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.missing_target_means_step_done = true
        elseif tostring(step.key or "") == "click_skill_upgrade_image" then
            step.missing_image_means_done = false
            step.missing_image_means_step_done = true
            step.cleanup_back_before_finish = true
            step.repeat_image_until_missing = true
            step.repeat_image_until_missing_max_count = 30
            step.repeat_image_until_missing_interval_ms = 180
            if type(step.image_preset) == "table" then
                step.image_preset.click_repeat_count = 1
                step.image_preset.repeat_until_missing = true
                step.image_preset.repeat_until_missing_max_count = 30
                step.image_preset.repeat_until_missing_interval_ms = 180
            end
        end
    end

    local function fixed_click_step(key, label, client_x, client_y, ratio_x, ratio_y, wait_after_ms)
        return make_maintenance_fixed_click_step({
            key = key,
            label = label,
            fixed_client_x = client_x,
            fixed_client_y = client_y,
            fixed_ratio_x = ratio_x,
            fixed_ratio_y = ratio_y,
            wait_after_ms = wait_after_ms or 500
        })
    end

    local function maintenance_locator_step(key, label, include_patterns, hint_client_x, hint_client_y, hint_ratio_x, hint_ratio_y, hint_max_distance, wait_after_ms)
        return make_maintenance_locator_step({
            key = key,
            label = label,
            include_patterns = include_patterns,
            hint_client_x = hint_client_x,
            hint_client_y = hint_client_y,
            hint_ratio_x = hint_ratio_x,
            hint_ratio_y = hint_ratio_y,
            hint_max_distance = hint_max_distance or 80,
            wait_after_ms = wait_after_ms or 650
        })
    end

    steps[#steps + 1] = {
        key = "level_4_open_fast_entrance_menu_after_skill_image",
        label = "技能天赋菜单按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
        },
        hint_client_x = 1383.688110,
        hint_client_y = 52.706509,
        hint_ratio_x = 0.961562,
        hint_ratio_y = 0.058563,
        hint_max_distance = 100,
        wait_after_ms = 800
    }
    steps[#steps + 1] = {
        key = "level_4_open_skill_panel_after_skill_image",
        label = "技能按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 1249.024658,
        hint_client_y = 155.104156,
        hint_ratio_x = 0.867981,
        hint_ratio_y = 0.172338,
        hint_max_distance = 90,
        wait_after_ms = 1000
    }
    steps[#steps + 1] = fixed_click_step(
        "level_4_skill_tab_spell_control",
        "4级技能页标签固定点击",
        725.00,
        278.00,
        0.503472,
        0.308889,
        700
    )
    steps[#steps + 1] = fixed_click_step(
        "level_4_skill_search_focus",
        "4级技能搜索输入框",
        367.00,
        687.00,
        0.254861,
        0.763333,
        250
    )
    steps[#steps + 1] = {
        kind = "type_text",
        key = "level_4_skill_search_spell_control_text",
        label = "输入范围扩大",
        text = "范围扩大",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    steps[#steps + 1] = maintenance_locator_step(
        "level_4_skill_search_spell_control_button",
        "范围扩大搜索按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.SearchBtn"
        },
        521.041626,
        706.869751,
        0.361834,
        0.785411,
        30,
        700
    )
    steps[#steps + 1] = maintenance_locator_step(
        "level_4_skill_select_spell_control_store_item",
        "范围扩大商店结果按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Store.WidgetTree.SkillBagStoreEquipItem_C.WidgetTree.ClickBtn"
        },
        241.362122,
        273.150360,
        0.167613,
        0.303500,
        30,
        700
    )
    steps[#steps + 1] = maintenance_locator_step(
        "level_4_skill_get_spell_control",
        "范围扩大获取按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        747.855347,
        456.843689,
        0.519344,
        0.507604,
        30,
        700
    )
    steps[#steps + 1] = maintenance_locator_step(
        "level_4_skill_select_spell_control_bag_item",
        "范围扩大背包结果按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Bag.WidgetTree.SkillBagBackpackEquipItem_C.WidgetTree.ClickBtn"
        },
        663.643738,
        273.345367,
        0.460864,
        0.303717,
        30,
        700
    )
    steps[#steps + 1] = maintenance_locator_step(
        "level_4_skill_install_spell_control",
        "范围扩大安装按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        415.214478,
        456.843689,
        0.288343,
        0.507604,
        30,
        700
    )
    steps[#steps + 1] = {
        key = "back_from_skill_panel_after_spell_control_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1369.400024,
        hint_client_y = 37.000000,
        hint_ratio_x = 0.950972,
        hint_ratio_y = 0.041111,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    local level_5_life_potion_plan = {
        key = "level_5_skill_life_potion_sequence",
        label = "5级技能：添加生命药水",
        require_available_points = false,
        close_with_escape = false,
        steps = {}
    }
    mark_skill_pre_add_bag_cleanup(level_5_life_potion_plan)
    local level_5_life_potion_steps = level_5_life_potion_plan.steps
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = {
        key = "level_5_life_potion_open_fast_entrance_menu",
        label = "技能天赋菜单按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
        },
        hint_client_x = 1383.688110,
        hint_client_y = 52.706509,
        hint_ratio_x = 0.961562,
        hint_ratio_y = 0.058563,
        hint_max_distance = 100,
        wait_after_ms = 800
    }
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = {
        key = "level_5_life_potion_open_skill_panel",
        label = "技能按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 1249.024658,
        hint_client_y = 155.104156,
        hint_ratio_x = 0.867981,
        hint_ratio_y = 0.172338,
        hint_max_distance = 90,
        wait_after_ms = 1000
    }
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = {
        key = "level_5_skill_select_life_potion_slot",
        label = "生命药水技能列表项按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.SkillexViewItem.WidgetTree.ClickBtn"
        },
        hint_client_x = 65.683357,
        hint_client_y = 409.164001,
        hint_ratio_x = 0.045613,
        hint_ratio_y = 0.454627,
        hint_max_distance = 30,
        target_poll_count = 15,
        target_poll_interval_ms = 100,
        fixed_fallback_client_x = 94.00,
        fixed_fallback_client_y = 405.00,
        fixed_fallback_ratio_x = 0.065278,
        fixed_fallback_ratio_y = 0.450000,
        fixed_fallback_prefer_ratio = true,
        fixed_fallback_mouse_mode = "api",
        fixed_fallback_click_delay_ms = 50,
        fixed_fallback_hover_delay_ms = 80,
        wait_after_ms = 700
    }
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = fixed_click_step(
        "level_5_skill_tab_life_potion",
        "生命药水技能页标签按钮固定点击",
        724.00,
        456.00,
        0.502778,
        0.506667,
        700
    )
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = fixed_click_step(
        "level_5_skill_life_potion_search_focus",
        "生命药水搜索输入框",
        367.00,
        691.00,
        0.254861,
        0.767778,
        250
    )
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = {
        kind = "type_text",
        key = "level_5_skill_search_life_potion_text",
        label = "输入生命药水",
        text = "生命药水",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = maintenance_locator_step(
        "level_5_skill_search_life_potion_button",
        "生命药水搜索按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.SearchBtn"
        },
        521.041626,
        706.869751,
        0.361834,
        0.785411,
        30,
        700
    )
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = maintenance_locator_step(
        "level_5_skill_select_life_potion_store_item",
        "生命药水商店结果按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Store.WidgetTree.SkillBagStoreEquipItem_C.WidgetTree.ClickBtn"
        },
        241.362122,
        273.150360,
        0.167613,
        0.303500,
        30,
        700
    )
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = maintenance_locator_step(
        "level_5_skill_get_life_potion",
        "生命药水获取按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        747.855347,
        496.950378,
        0.519344,
        0.552167,
        30,
        700
    )
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = maintenance_locator_step(
        "level_5_skill_select_life_potion_bag_item",
        "生命药水背包结果按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Bag.WidgetTree.SkillBagBackpackEquipItem_C.WidgetTree.ClickBtn"
        },
        663.643738,
        273.345367,
        0.460864,
        0.303717,
        30,
        700
    )
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = maintenance_locator_step(
        "level_5_skill_install_life_potion",
        "生命药水安装按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        415.214478,
        496.950378,
        0.288343,
        0.552167,
        30,
        700
    )
    level_5_life_potion_steps[#level_5_life_potion_steps + 1] = {
        key = "level_5_back_from_skill_panel_after_life_potion_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1369.400024,
        hint_client_y = 37.000000,
        hint_ratio_x = 0.950972,
        hint_ratio_y = 0.041111,
        hint_max_distance = 30,
        wait_after_ms = 500
    }
    level_4_skill_plan.steps = steps

    local level_5_range_skill_plan = clone_plain_table(level_4_skill_plan)
    level_5_range_skill_plan.key = "level_5_skill_upgrade_range_sequence"
    level_5_range_skill_plan.label = "5级技能：找图升级并配置范围扩大"
    for _, step in ipairs(type(level_5_range_skill_plan.steps) == "table" and level_5_range_skill_plan.steps or {}) do
        if type(step) == "table" then
            if type(step.key) == "string" then
                if step.key:find("^level_4_") ~= nil then
                    step.key = step.key:gsub("^level_4_", "level_5_range_")
                elseif step.key:find("^level_5_range_") == nil then
                    step.key = "level_5_range_" .. step.key
                end
            end
            if type(step.label) == "string" then
                step.label = step.label:gsub("4级", "5级")
            end
        end
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_extra_by_level[5] = level_5_range_skill_plan
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[5] = level_5_life_potion_plan

    local level_8_skill_plan = {
        key = "level_8_skill_add_blunt_sequence",
        label = "8级技能：添加钝化",
        require_available_points = false,
        close_with_escape = false,
        steps = {}
    }
    mark_skill_pre_add_bag_cleanup(level_8_skill_plan)
    local level_8_steps = level_8_skill_plan.steps
    level_8_steps[#level_8_steps + 1] = {
        key = "level_8_open_fast_entrance_menu_for_blunt",
        label = "技能天赋菜单按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
        },
        hint_client_x = 1383.688110,
        hint_client_y = 52.706509,
        hint_ratio_x = 0.961562,
        hint_ratio_y = 0.058563,
        hint_max_distance = 100,
        wait_after_ms = 800
    }
    level_8_steps[#level_8_steps + 1] = {
        key = "level_8_open_skill_panel_for_blunt",
        label = "技能按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 1249.024658,
        hint_client_y = 155.104156,
        hint_ratio_x = 0.867981,
        hint_ratio_y = 0.172338,
        hint_max_distance = 90,
        wait_after_ms = 1000
    }
    local level_8_blunt_first_click_step = fixed_click_step(
        "level_8_skill_blunt_fixed_click_564_360",
        "8级钝化固定点击",
        564.00,
        360.00,
        0.391667,
        0.400000,
        350
    )
    level_8_blunt_first_click_step.click_repeat_count = 2
    level_8_blunt_first_click_step.click_repeat_interval_min_ms = 500
    level_8_blunt_first_click_step.click_repeat_interval_max_ms = 800
    level_8_steps[#level_8_steps + 1] = level_8_blunt_first_click_step
    level_8_steps[#level_8_steps + 1] = fixed_click_step(
        "level_8_skill_blunt_search_focus",
        "8级钝化搜索输入框",
        339.00,
        691.00,
        0.235417,
        0.767778,
        250
    )
    level_8_steps[#level_8_steps + 1] = {
        kind = "type_text",
        key = "level_8_skill_search_blunt_text",
        label = "输入钝化",
        text = "钝化",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_8_steps[#level_8_steps + 1] = maintenance_locator_step(
        "level_8_skill_search_blunt_button",
        "钝化搜索按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.SearchBtn"
        },
        521.041626,
        706.869751,
        0.361834,
        0.785411,
        30,
        700
    )
    level_8_steps[#level_8_steps + 1] = maintenance_locator_step(
        "level_8_skill_select_blunt_store_item",
        "钝化商店结果按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Store.WidgetTree.SkillBagStoreEquipItem_C.WidgetTree.ClickBtn"
        },
        241.362122,
        273.150360,
        0.167613,
        0.303500,
        30,
        700
    )
    level_8_steps[#level_8_steps + 1] = maintenance_locator_step(
        "level_8_skill_get_blunt",
        "钝化获取按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        747.855347,
        435.949280,
        0.519344,
        0.484388,
        30,
        700
    )
    level_8_steps[#level_8_steps + 1] = maintenance_locator_step(
        "level_8_skill_select_blunt_bag_item",
        "钝化背包结果按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Bag.WidgetTree.SkillBagBackpackEquipItem_C.WidgetTree.ClickBtn"
        },
        663.643738,
        273.345367,
        0.460864,
        0.303717,
        30,
        700
    )
    level_8_steps[#level_8_steps + 1] = maintenance_locator_step(
        "level_8_skill_install_blunt",
        "钝化安装按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        415.214478,
        435.949280,
        0.288343,
        0.484388,
        30,
        700
    )
    level_8_steps[#level_8_steps + 1] = {
        key = "level_8_back_from_skill_panel_after_blunt_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1369.400024,
        hint_client_y = 37.000000,
        hint_ratio_x = 0.950972,
        hint_ratio_y = 0.041111,
        hint_max_distance = 30,
        wait_after_ms = 500
    }
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[8] = level_8_skill_plan

    local level_12_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_12_skill_plan.key = "level_12_skill_add_emergency_sequence"
    level_12_skill_plan.label = "12级技能：添加应急回复"
    level_12_skill_plan.close_with_escape = false
    mark_skill_pre_add_bag_cleanup(level_12_skill_plan)

    local level_12_steps = type(level_12_skill_plan.steps) == "table" and level_12_skill_plan.steps or {}
    if #level_12_steps > 0 and tostring(level_12_steps[#level_12_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_12_steps, #level_12_steps)
    end
    for _, step in ipairs(level_12_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_12_open_skill_add_panel"
            step.label = "12级技能加点入口按钮"
            step.missing_target_means_step_done = true
        elseif tostring(step.key or "") == "click_skill_upgrade_image" then
            step.key = "level_12_click_skill_upgrade_image"
            step.label = "12级技能升级找图按钮"
            step.missing_image_means_done = false
            step.missing_image_means_step_done = true
            step.cleanup_back_before_finish = true
            step.repeat_image_until_missing = true
            step.repeat_image_until_missing_max_count = 30
            step.repeat_image_until_missing_interval_ms = 180
            if type(step.image_preset) == "table" then
                step.image_preset.click_repeat_count = 1
                step.image_preset.repeat_until_missing = true
                step.image_preset.repeat_until_missing_max_count = 30
                step.image_preset.repeat_until_missing_interval_ms = 180
            end
        end
    end

    level_12_steps[#level_12_steps + 1] = {
        key = "level_12_open_fast_entrance_menu_after_skill_image",
        label = "技能天赋菜单按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
        },
        hint_client_x = 1383.688110,
        hint_client_y = 52.706509,
        hint_ratio_x = 0.961562,
        hint_ratio_y = 0.058563,
        hint_max_distance = 100,
        wait_after_ms = 800
    }
    level_12_steps[#level_12_steps + 1] = {
        key = "level_12_open_skill_panel_after_skill_image",
        label = "技能按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 1249.024658,
        hint_client_y = 155.104156,
        hint_ratio_x = 0.867981,
        hint_ratio_y = 0.172338,
        hint_max_distance = 90,
        wait_after_ms = 1000
    }
    level_12_steps[#level_12_steps + 1] = maintenance_locator_step(
        "level_12_skill_select_list_item",
        "12级技能列表项按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.SkillexViewItem.WidgetTree.ClickBtn"
        },
        65.683357,
        284.575226,
        0.045613,
        0.316195,
        30,
        500
    )
    level_12_steps[#level_12_steps + 1] = fixed_click_step(
        "level_12_skill_fixed_click_725_276",
        "12级技能固定点击1",
        727.00,
        275.00,
        0.504861,
        0.305556,
        500
    )
    level_12_steps[#level_12_steps + 1] = fixed_click_step(
        "level_12_skill_search_focus",
        "12级技能搜索输入框",
        360.00,
        689.00,
        0.250000,
        0.765556,
        250
    )
    level_12_steps[#level_12_steps + 1] = {
        kind = "type_text",
        key = "level_12_skill_search_emergency_reply_text",
        label = "输入应急回复",
        text = "应急回复",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_12_steps[#level_12_steps + 1] = maintenance_locator_step(
        "level_12_skill_search_confirm",
        "12级技能搜索按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.SearchBtn"
        },
        521.041626,
        706.869751,
        0.361834,
        0.785411,
        30,
        700
    )
    level_12_steps[#level_12_steps + 1] = {
        key = "level_12_skill_select_store_lv4",
        label = "12级技能商店结果按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Store.WidgetTree.SkillBagStoreEquipItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 241.362122,
        hint_client_y = 273.150360,
        hint_ratio_x = 0.167613,
        hint_ratio_y = 0.303500,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_12_steps[#level_12_steps + 1] = {
        key = "level_12_skill_get_button",
        label = "12级技能获取按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        hint_client_x = 747.855347,
        hint_client_y = 414.349304,
        hint_ratio_x = 0.519344,
        hint_ratio_y = 0.460388,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_12_steps[#level_12_steps + 1] = {
        key = "level_12_skill_select_bag_lv4",
        label = "12级技能背包结果按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Bag.WidgetTree.SkillBagBackpackEquipItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 663.643738,
        hint_client_y = 273.345367,
        hint_ratio_x = 0.460864,
        hint_ratio_y = 0.303717,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_12_steps[#level_12_steps + 1] = {
        key = "level_12_skill_install_button",
        label = "12级技能安装按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        hint_client_x = 415.214478,
        hint_client_y = 414.349304,
        hint_ratio_x = 0.288343,
        hint_ratio_y = 0.460388,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_12_steps[#level_12_steps + 1] = {
        key = "back_from_skill_panel_after_emergency_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1369.400024,
        hint_client_y = 37.000000,
        hint_ratio_x = 0.950972,
        hint_ratio_y = 0.041111,
        hint_max_distance = 30,
        wait_after_ms = 500
    }

    level_12_skill_plan.steps = level_12_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[12] = level_12_skill_plan

    local level_21_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_21_skill_plan.key = "level_21_skill_add_element_and_mana_sequence"
    level_21_skill_plan.label = "21级技能：配置痛楚加剧并添加魔力沸腾"
    level_21_skill_plan.close_with_escape = false
    mark_skill_pre_add_bag_cleanup(level_21_skill_plan)

    local level_21_steps = type(level_21_skill_plan.steps) == "table" and level_21_skill_plan.steps or {}
    if #level_21_steps > 0 and tostring(level_21_steps[#level_21_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_21_steps, #level_21_steps)
    end
    for _, step in ipairs(level_21_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_21_open_skill_add_panel"
            step.label = "21级技能加点入口按钮"
            step.missing_target_means_step_done = true
        elseif tostring(step.key or "") == "click_skill_upgrade_image" then
            step.key = "level_21_click_skill_upgrade_image"
            step.label = "21级技能升级找图按钮"
            step.missing_image_means_done = false
            step.missing_image_means_step_done = true
            step.cleanup_back_before_finish = true
            step.repeat_image_until_missing = true
            step.repeat_image_until_missing_max_count = 30
            step.repeat_image_until_missing_interval_ms = 180
            if type(step.image_preset) == "table" then
                step.image_preset.click_repeat_count = 1
                step.image_preset.repeat_until_missing = true
                step.image_preset.repeat_until_missing_max_count = 30
                step.image_preset.repeat_until_missing_interval_ms = 180
            end
        end
    end
    for index = #level_21_steps, 1, -1 do
        local step = level_21_steps[index]
        local key = tostring(type(step) == "table" and step.key or "")
        if key == "level_21_open_skill_add_panel" then
            table.remove(level_21_steps, index)
        end
    end

    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_open_fast_entrance_menu_after_skill_image",
        label = "技能天赋菜单按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
        },
        hint_client_x = 1383.688110,
        hint_client_y = 52.706509,
        hint_ratio_x = 0.961562,
        hint_ratio_y = 0.058563,
        hint_max_distance = 100,
        wait_after_ms = 800
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_open_skill_panel_after_skill_image",
        label = "技能按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 1249.024658,
        hint_client_y = 155.104156,
        hint_ratio_x = 0.867981,
        hint_ratio_y = 0.172338,
        hint_max_distance = 90,
        wait_after_ms = 1000
    }
    local level_21_pain_first_click_step = fixed_click_step(
        "level_21_skill_element_fusion_fixed_click_564_551",
        "21级痛楚加剧固定点击",
        564.00,
        551.00,
        0.391667,
        0.612222,
        350
    )
    level_21_pain_first_click_step.click_repeat_count = 2
    level_21_pain_first_click_step.click_repeat_interval_min_ms = 500
    level_21_pain_first_click_step.click_repeat_interval_max_ms = 800
    level_21_steps[#level_21_steps + 1] = level_21_pain_first_click_step
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_element_search_focus",
        "21级痛楚加剧搜索输入框",
        354.00,
        690.00,
        0.245833,
        0.766667,
        250
    )
    level_21_steps[#level_21_steps + 1] = {
        kind = "type_text",
        key = "level_21_skill_search_element_text",
        label = "输入痛楚加剧",
        text = "痛楚加剧",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_element_search_button",
        label = "21级痛楚加剧搜索按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.SearchBtn"
        },
        hint_client_x = 521.041626,
        hint_client_y = 706.869751,
        hint_ratio_x = 0.361834,
        hint_ratio_y = 0.785411,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_select_element_store_lv6",
        label = "21级痛楚加剧商店结果按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Store.WidgetTree.SkillBagStoreEquipItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 241.362122,
        hint_client_y = 273.150360,
        hint_ratio_x = 0.167613,
        hint_ratio_y = 0.303500,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_get_element_button",
        label = "21级痛楚加剧获取按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        hint_client_x = 747.855347,
        hint_client_y = 438.619690,
        hint_ratio_x = 0.519344,
        hint_ratio_y = 0.487355,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_select_element_bag_lv6",
        label = "21级痛楚加剧背包结果按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Bag.WidgetTree.SkillBagBackpackEquipItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 663.643738,
        hint_client_y = 273.345367,
        hint_ratio_x = 0.460864,
        hint_ratio_y = 0.303717,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_install_element_button",
        label = "21级痛楚加剧安装按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        hint_client_x = 415.214478,
        hint_client_y = 438.619690,
        hint_ratio_x = 0.288343,
        hint_ratio_y = 0.487355,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_select_list_item_after_element",
        label = "21级技能列表项按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.SkillexViewItem.WidgetTree.ClickBtn"
        },
        hint_client_x = 65.683357,
        hint_client_y = 471.458435,
        hint_ratio_x = 0.045613,
        hint_ratio_y = 0.523843,
        hint_max_distance = 30,
        wait_after_ms = 500
    }
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_mana_fixed_click_725_459",
        "21级魔力固定点击1",
        725.00,
        459.00,
        0.503472,
        0.510000,
        350
    )
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_mana_search_focus",
        "21级魔力搜索输入框",
        359.00,
        691.00,
        0.249306,
        0.767778,
        250
    )
    level_21_steps[#level_21_steps + 1] = {
        kind = "type_text",
        key = "level_21_skill_search_mana_text",
        label = "输入魔力沸腾",
        text = "魔力沸腾",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_mana_array_search_button",
        label = "21级魔力沸腾搜索按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.SearchBtn"
        },
        hint_client_x = 521.041626,
        hint_client_y = 706.869751,
        hint_ratio_x = 0.361834,
        hint_ratio_y = 0.785411,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_select_mana_store_lv6",
        label = "21级魔力沸腾商店结果按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Store.WidgetTree.SkillBagStoreEquipItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 241.362122,
        hint_client_y = 273.150360,
        hint_ratio_x = 0.167613,
        hint_ratio_y = 0.303500,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_get_mana_button",
        label = "21级魔力沸腾获取按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        hint_client_x = 747.855347,
        hint_client_y = 414.349304,
        hint_ratio_x = 0.519344,
        hint_ratio_y = 0.460388,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_select_mana_bag_lv6",
        label = "21级魔力沸腾背包结果按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Bag.WidgetTree.SkillBagBackpackEquipItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 663.643738,
        hint_client_y = 273.345367,
        hint_ratio_x = 0.460864,
        hint_ratio_y = 0.303717,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "level_21_skill_install_mana_button",
        label = "21级魔力沸腾安装按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        hint_client_x = 415.214478,
        hint_client_y = 414.349304,
        hint_ratio_x = 0.288343,
        hint_ratio_y = 0.460388,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_21_steps[#level_21_steps + 1] = {
        key = "back_from_skill_panel_after_element_and_mana_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1369.400024,
        hint_client_y = 37.000000,
        hint_ratio_x = 0.950972,
        hint_ratio_y = 0.041111,
        hint_max_distance = 30,
        wait_after_ms = 500
    }

    level_21_skill_plan.steps = level_21_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[21] = level_21_skill_plan

    local level_29_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_29_skill_plan.key = "level_29_skill_upgrade_sequence"
    level_29_skill_plan.label = "29级技能：找图升级并添加法术控制"
    level_29_skill_plan.close_with_escape = false
    mark_skill_pre_add_bag_cleanup(level_29_skill_plan)

    local level_29_steps = type(level_29_skill_plan.steps) == "table" and level_29_skill_plan.steps or {}
    if #level_29_steps > 0 and tostring(level_29_steps[#level_29_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_29_steps, #level_29_steps)
    end
    for _, step in ipairs(level_29_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_29_open_skill_add_panel"
            step.label = "29级技能加点入口按钮"
            step.missing_target_means_step_done = true
        elseif tostring(step.key or "") == "click_skill_upgrade_image" then
            step.key = "level_29_click_skill_upgrade_image"
            step.label = "29级技能升级找图按钮"
            step.missing_image_means_done = false
            step.missing_image_means_step_done = true
            step.cleanup_back_before_finish = true
            step.repeat_image_until_missing = true
            step.repeat_image_until_missing_max_count = 30
            step.repeat_image_until_missing_interval_ms = 180
            if type(step.image_preset) == "table" then
                step.image_preset.click_repeat_count = 1
                step.image_preset.repeat_until_missing = true
                step.image_preset.repeat_until_missing_max_count = 30
                step.image_preset.repeat_until_missing_interval_ms = 180
            end
        end
    end

    level_29_steps[#level_29_steps + 1] = {
        key = "level_29_open_fast_entrance_menu_after_skill_image",
        label = "技能天赋菜单按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
        },
        hint_client_x = 1383.688110,
        hint_client_y = 52.706509,
        hint_ratio_x = 0.961562,
        hint_ratio_y = 0.058563,
        hint_max_distance = 100,
        wait_after_ms = 800
    }
    level_29_steps[#level_29_steps + 1] = {
        key = "level_29_open_skill_panel_after_skill_image",
        label = "技能按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 1249.024658,
        hint_client_y = 155.104156,
        hint_ratio_x = 0.867981,
        hint_ratio_y = 0.172338,
        hint_max_distance = 90,
        wait_after_ms = 1000
    }
    level_29_steps[#level_29_steps + 1] = {
        key = "level_29_skill_select_list_item",
        label = "29级技能列表项按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.SkillexViewItem.WidgetTree.ClickBtn"
        },
        hint_client_x = 65.683357,
        hint_client_y = 346.869629,
        hint_ratio_x = 0.045613,
        hint_ratio_y = 0.385411,
        hint_max_distance = 30,
        wait_after_ms = 500
    }
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_fixed_click_725_273",
        "29级技能固定点击2",
        725.00,
        273.00,
        0.503472,
        0.303333,
        350
    )
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_search_focus",
        "29级技能搜索输入框",
        337.00,
        688.00,
        0.234028,
        0.764444,
        250
    )
    level_29_steps[#level_29_steps + 1] = {
        kind = "type_text",
        key = "level_29_skill_search_spell_control_text",
        label = "输入法术控制",
        text = "法术控制",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_29_steps[#level_29_steps + 1] = {
        key = "level_29_skill_spell_control_search_button",
        label = "29级法术控制搜索按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.SearchBtn"
        },
        hint_client_x = 521.041626,
        hint_client_y = 706.869751,
        hint_ratio_x = 0.361834,
        hint_ratio_y = 0.785411,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_29_steps[#level_29_steps + 1] = {
        key = "level_29_skill_select_spell_control_store_lv7",
        label = "29级法术控制商店结果按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Store.WidgetTree.SkillBagStoreEquipItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 241.362122,
        hint_client_y = 273.150360,
        hint_ratio_x = 0.167613,
        hint_ratio_y = 0.303500,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_29_steps[#level_29_steps + 1] = {
        key = "level_29_skill_get_spell_control_button",
        label = "29级法术控制获取按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        hint_client_x = 747.855347,
        hint_client_y = 456.843689,
        hint_ratio_x = 0.519344,
        hint_ratio_y = 0.507604,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_29_steps[#level_29_steps + 1] = {
        key = "level_29_skill_select_spell_control_bag_lv7",
        label = "29级法术控制背包结果按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Bag.WidgetTree.SkillBagBackpackEquipItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 663.643738,
        hint_client_y = 273.345367,
        hint_ratio_x = 0.460864,
        hint_ratio_y = 0.303717,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_29_steps[#level_29_steps + 1] = {
        key = "level_29_skill_install_spell_control_button",
        label = "29级法术控制安装按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        hint_client_x = 415.214478,
        hint_client_y = 456.843689,
        hint_ratio_x = 0.288343,
        hint_ratio_y = 0.507604,
        hint_max_distance = 30,
        wait_after_ms = 700
    }
    level_29_steps[#level_29_steps + 1] = {
        key = "back_from_skill_panel_after_spell_control_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1369.400024,
        hint_client_y = 37.000000,
        hint_ratio_x = 0.950972,
        hint_ratio_y = 0.041111,
        hint_max_distance = 30,
        wait_after_ms = 500
    }

    level_29_skill_plan.steps = level_29_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[29] = level_29_skill_plan

    local level_34_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_34_skill_plan.key = "level_34_skill_upgrade_and_add_erosion_infusion_sequence"
    level_34_skill_plan.label = "34级技能：找图升级并添加侵蚀贯注"
    level_34_skill_plan.close_with_escape = false
    mark_skill_pre_add_bag_cleanup(level_34_skill_plan)

    local level_34_steps = type(level_34_skill_plan.steps) == "table" and level_34_skill_plan.steps or {}
    if #level_34_steps > 0 and tostring(level_34_steps[#level_34_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_34_steps, #level_34_steps)
    end
    for _, step in ipairs(level_34_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_34_open_skill_add_panel"
            step.label = "34级技能加点入口按钮"
            step.missing_target_means_step_done = true
        elseif tostring(step.key or "") == "click_skill_upgrade_image" then
            step.key = "level_34_click_skill_upgrade_image"
            step.label = "34级技能升级找图按钮"
            step.missing_image_means_done = false
            step.missing_image_means_step_done = true
            step.cleanup_back_before_finish = true
            step.repeat_image_until_missing = true
            step.repeat_image_until_missing_max_count = 30
            step.repeat_image_until_missing_interval_ms = 180
            if type(step.image_preset) == "table" then
                step.image_preset.click_repeat_count = 1
                step.image_preset.repeat_until_missing = true
                step.image_preset.repeat_until_missing_max_count = 30
                step.image_preset.repeat_until_missing_interval_ms = 180
            end
        end
    end

    level_34_steps[#level_34_steps + 1] = {
        key = "level_34_open_fast_entrance_menu_after_skill_image",
        label = "技能天赋菜单按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
        },
        hint_client_x = 1383.688110,
        hint_client_y = 52.706509,
        hint_ratio_x = 0.961562,
        hint_ratio_y = 0.058563,
        hint_max_distance = 100,
        wait_after_ms = 800
    }
    level_34_steps[#level_34_steps + 1] = {
        key = "level_34_open_skill_panel_after_skill_image",
        label = "技能按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 1249.024658,
        hint_client_y = 155.104156,
        hint_ratio_x = 0.867981,
        hint_ratio_y = 0.172338,
        hint_max_distance = 90,
        wait_after_ms = 1000
    }
    level_34_steps[#level_34_steps + 1] = maintenance_locator_step(
        "level_34_select_skill_list_item",
        "34级技能列表项按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.SkillexViewItem.WidgetTree.ClickBtn"
        },
        65.683357,
        546.113403,
        0.045613,
        0.606793,
        30,
        650
    )
    level_34_steps[#level_34_steps + 1] = fixed_click_step(
        "level_34_select_skill_page_tab",
        "34级技能页标签固定点击",
        726.00,
        458.00,
        0.504167,
        0.508889,
        650
    )
    level_34_steps[#level_34_steps + 1] = fixed_click_step(
        "level_34_skill_erosion_infusion_search_focus",
        "34级侵蚀贯注搜索输入框",
        325.00,
        690.00,
        0.225694,
        0.766667,
        250
    )
    level_34_steps[#level_34_steps + 1] = {
        kind = "type_text",
        key = "level_34_skill_search_erosion_infusion_text",
        label = "输入侵蚀贯注",
        text = "侵蚀贯注",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_34_steps[#level_34_steps + 1] = maintenance_locator_step(
        "level_34_skill_search_button",
        "34级技能搜索按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.SearchBtn"
        },
        521.041626,
        706.869751,
        0.361834,
        0.785411,
        30,
        700
    )
    level_34_steps[#level_34_steps + 1] = maintenance_locator_step(
        "level_34_select_store_skill",
        "34级商店技能按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Store.WidgetTree.SkillBagStoreEquipItem_C.WidgetTree.ClickBtn"
        },
        241.362122,
        273.150360,
        0.167613,
        0.303500,
        30,
        700
    )
    level_34_steps[#level_34_steps + 1] = maintenance_locator_step(
        "level_34_get_erosion_infusion_skill",
        "34级获取按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        747.855347,
        542.993591,
        0.519344,
        0.603326,
        30,
        700
    )
    level_34_steps[#level_34_steps + 1] = maintenance_locator_step(
        "level_34_select_bag_skill",
        "34级背包技能按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Bag.WidgetTree.SkillBagBackpackEquipItem_C.WidgetTree.ClickBtn"
        },
        663.643738,
        273.345367,
        0.460864,
        0.303717,
        30,
        700
    )
    level_34_steps[#level_34_steps + 1] = maintenance_locator_step(
        "level_34_install_erosion_infusion_skill",
        "34级安装按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        415.214478,
        542.993591,
        0.288343,
        0.603326,
        30,
        700
    )
    level_34_steps[#level_34_steps + 1] = maintenance_locator_step(
        "level_34_activate_all_skills",
        "34级技能启用全部按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.ActiveAll"
        },
        593.887695,
        850.750366,
        0.412422,
        0.945278,
        30,
        700
    )
    level_34_steps[#level_34_steps + 1] = {
        key = "back_from_skill_panel_after_erosion_infusion_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1369.400024,
        hint_client_y = 37.000000,
        hint_ratio_x = 0.950972,
        hint_ratio_y = 0.041111,
        hint_max_distance = 30,
        wait_after_ms = 500
    }

    level_34_skill_plan.steps = level_34_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[34] = level_34_skill_plan

    local level_52_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_52_skill_plan.key = "level_52_skill_upgrade_and_add_spell_focus_sequence"
    level_52_skill_plan.label = "52级技能：找图升级并添加法术集中"
    level_52_skill_plan.close_with_escape = false
    mark_skill_pre_add_bag_cleanup(level_52_skill_plan)

    local level_52_steps = type(level_52_skill_plan.steps) == "table" and level_52_skill_plan.steps or {}
    if #level_52_steps > 0 and tostring(level_52_steps[#level_52_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_52_steps, #level_52_steps)
    end
    for _, step in ipairs(level_52_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_52_open_skill_add_panel"
            step.label = "52级技能加点入口按钮"
            step.missing_target_means_step_done = true
        elseif tostring(step.key or "") == "click_skill_upgrade_image" then
            step.key = "level_52_click_skill_upgrade_image"
            step.label = "52级技能升级找图按钮"
            step.missing_image_means_done = false
            step.missing_image_means_step_done = true
            step.cleanup_back_before_finish = true
            step.repeat_image_until_missing = true
            step.repeat_image_until_missing_max_count = 30
            step.repeat_image_until_missing_interval_ms = 180
            if type(step.image_preset) == "table" then
                step.image_preset.click_repeat_count = 1
                step.image_preset.repeat_until_missing = true
                step.image_preset.repeat_until_missing_max_count = 30
                step.image_preset.repeat_until_missing_interval_ms = 180
            end
        end
    end

    level_52_steps[#level_52_steps + 1] = {
        key = "level_52_open_fast_entrance_menu_after_skill_image",
        label = "技能天赋菜单按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
        },
        hint_client_x = 1383.688110,
        hint_client_y = 52.706509,
        hint_ratio_x = 0.961562,
        hint_ratio_y = 0.058563,
        hint_max_distance = 100,
        wait_after_ms = 800
    }
    level_52_steps[#level_52_steps + 1] = {
        key = "level_52_open_skill_panel_after_skill_image",
        label = "技能按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 1249.024658,
        hint_client_y = 155.104156,
        hint_ratio_x = 0.867981,
        hint_ratio_y = 0.172338,
        hint_max_distance = 90,
        wait_after_ms = 1000
    }
    level_52_steps[#level_52_steps + 1] = maintenance_locator_step(
        "level_52_select_skill_list_item",
        "52级技能列表项按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.SkillexViewItem.WidgetTree.ClickBtn"
        },
        72.683357,
        547.113403,
        0.050475,
        0.607904,
        30,
        650
    )
    level_52_steps[#level_52_steps + 1] = fixed_click_step(
        "level_52_select_skill_page_tab",
        "52级技能页标签固定点击",
        553.00,
        408.00,
        0.384028,
        0.453333,
        650
    )
    level_52_steps[#level_52_steps + 1] = fixed_click_step(
        "level_52_skill_spell_focus_search_focus",
        "52级法术集中搜索输入框",
        346.00,
        692.00,
        0.240278,
        0.768889,
        250
    )
    level_52_steps[#level_52_steps + 1] = {
        kind = "type_text",
        key = "level_52_skill_search_spell_focus_text",
        label = "输入法术集中",
        text = "法术集中",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_52_steps[#level_52_steps + 1] = maintenance_locator_step(
        "level_52_skill_search_button",
        "52级技能搜索按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.SearchBtn"
        },
        528.041626,
        707.869751,
        0.366696,
        0.786522,
        30,
        700
    )
    level_52_steps[#level_52_steps + 1] = maintenance_locator_step(
        "level_52_select_store_skill",
        "52级商店技能按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Store.WidgetTree.SkillBagStoreEquipItem_C.WidgetTree.ClickBtn"
        },
        248.362122,
        274.150360,
        0.172474,
        0.304612,
        30,
        700
    )
    level_52_steps[#level_52_steps + 1] = maintenance_locator_step(
        "level_52_get_spell_focus_skill",
        "52级获取按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        754.855347,
        482.114136,
        0.524205,
        0.535682,
        30,
        700
    )
    level_52_steps[#level_52_steps + 1] = maintenance_locator_step(
        "level_52_select_bag_skill",
        "52级背包技能按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.SkillBagItem_C.WidgetTree.FilterSkillGoodsItem_Bag.WidgetTree.SkillBagBackpackEquipItem_C.WidgetTree.ClickBtn"
        },
        670.643738,
        274.345367,
        0.465725,
        0.304828,
        30,
        700
    )
    level_52_steps[#level_52_steps + 1] = maintenance_locator_step(
        "level_52_install_spell_focus_skill",
        "52级安装按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.TipSkillHandItem_C.WidgetTree.ChangeBtn"
        },
        422.214478,
        482.114136,
        0.293204,
        0.535682,
        30,
        700
    )
    level_52_steps[#level_52_steps + 1] = maintenance_locator_step(
        "level_52_activate_all_skills",
        "52级技能启用全部按钮",
        {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.ActiveAll"
        },
        600.887695,
        851.750366,
        0.417283,
        0.946389,
        30,
        700
    )
    level_52_steps[#level_52_steps + 1] = {
        key = "back_from_skill_panel_after_spell_focus_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1376.400024,
        hint_client_y = 38.000000,
        hint_ratio_x = 0.955833,
        hint_ratio_y = 0.042222,
        hint_max_distance = 30,
        wait_after_ms = 500
    }

    level_52_skill_plan.steps = level_52_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[52] = level_52_skill_plan

    -- Level 43 fixed-click add-skill plan is disabled; this level falls back to the default skill upgrade flow.

end

apply_skill_pre_add_bag_cleanup_steps()

do
    M.LEVEL_UP_MAINTENANCE_CONFIG.contract_by_level[18] = make_contract_initial_and_second_setup_plan({
        level = 18,
        key = "level_18_contract_setup",
        label = "18级契灵：配置契约面板",
        open_menu_label = "技能天赋菜单按钮",
        open_panel_label = "契灵面板按钮",
        back_label = "契灵返回固定点击",
        click_label_prefix = "18级契灵固定点击"
    })

    for _, level in ipairs({ 20, 24, 29, 32, 36, 41, 44, 49, 53, 57, 58 }) do
        M.LEVEL_UP_MAINTENANCE_CONFIG.contract_by_level[level] = make_contract_second_setup_plan({
            level = level,
            key = "level_" .. tostring(level) .. "_contract_second_setup",
            label = tostring(level) .. "级契灵：第二套契约点击配置",
            open_menu_label = "技能天赋菜单按钮",
            open_panel_label = "契灵面板按钮",
            back_label = "契灵返回固定点击",
            click_label_prefix = tostring(level) .. "级契灵固定点击"
        })
    end

    local function level_43_contract_button_step(key, label, button_name, client_x, client_y, ratio_x, ratio_y, wait_after_ms)
        return make_maintenance_locator_step({
            key = key,
            label = label,
            distance_button_name = button_name,
            include_patterns = {
                button_name
            },
            hint_client_x = client_x,
            hint_client_y = client_y,
            hint_ratio_x = ratio_x,
            hint_ratio_y = ratio_y,
            hint_max_distance = 30,
            wait_after_ms = wait_after_ms or 500
        })
    end

    local level_43_contract_plan = make_contract_second_setup_plan({
        level = 43,
        key = "level_43_contract_special_setup",
        label = "43级契灵：特殊点击配置",
        open_menu_label = "技能天赋菜单按钮",
        open_panel_label = "契灵面板按钮",
        back_label = "43级契灵返回固定点击"
    })
    local level_43_contract_base_steps = type(level_43_contract_plan.steps) == "table" and level_43_contract_plan.steps or {}
    level_43_contract_plan.steps = {
        clone_plain_table(level_43_contract_base_steps[1]),
        clone_plain_table(level_43_contract_base_steps[2]),
        level_43_contract_button_step(
            "level_43_contract_battle_pet_slot",
            "43级契灵战斗契灵槽按钮",
            "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.ServantEquipSlot.WidgetTree.ServantEquipSlotItem.WidgetTree.Btn",
            191.481232,
            319.260803,
            0.132973,
            0.354734,
            500
        ),
        level_43_contract_button_step(
            "level_43_contract_all_tab",
            "43级契灵全部按钮",
            "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.ServantList.WidgetTree.PetComTabItem.WidgetTree.Btn",
            256.943970,
            160.471985,
            0.178433,
            0.178302,
            500
        ),
        level_43_contract_button_step(
            "level_43_contract_new_filter",
            "43级契灵新按钮",
            "UIButton Transient.GameEngine.CoreGameInstance.PetPresetDropBtnItem_C.WidgetTree.ClickBtn",
            257.851196,
            311.067200,
            0.179063,
            0.345630,
            500
        ),
        level_43_contract_button_step(
            "level_43_contract_pet_list_item",
            "43级契灵契灵列表按钮",
            "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.ServantList.WidgetTree.PetItem_C.WidgetTree.Btn",
            190.718399,
            204.017593,
            0.132443,
            0.226686,
            500
        ),
        level_43_contract_button_step(
            "level_43_contract_bind_pet",
            "43级契灵结契按钮",
            "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.ChangeBtn",
            1281.087036,
            849.094360,
            0.889644,
            0.943438,
            700
        ),
        level_43_contract_button_step(
            "level_43_contract_special_back_1",
            "43级契灵返回按钮1",
            "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.UITitleItem.WidgetTree.BackBtn",
            30.143999,
            38.000000,
            0.020933,
            0.042222,
            700
        ),
        level_43_contract_button_step(
            "level_43_contract_special_back_2",
            "43级契灵返回按钮2",
            "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.UITitleItem.WidgetTree.BackBtn",
            30.143999,
            38.000000,
            0.020933,
            0.042222,
            700
        )
    }
    M.LEVEL_UP_MAINTENANCE_CONFIG.contract_by_level[43] = level_43_contract_plan
end

do
    local function make_hotkey_panel_open_step(source_step, panel_kind)
        source_step = type(source_step) == "table" and source_step or {}
        local key = tostring(source_step.key or "")
        local is_talent = panel_kind == "talent"
        local is_contract = panel_kind == "contract"
        local verify_pattern = "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        local label = "按K打开技能面板"
        local key_vk = 75
        local hint_client_x = 1369.400024
        local hint_client_y = 37.000000
        local hint_ratio_x = 0.950972
        local hint_ratio_y = 0.041111
        if is_talent then
            verify_pattern = "UIButton Transient.GameEngine.CoreGameInstance.Talent_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
            label = "按P打开天赋面板"
            key_vk = 80
            hint_client_x = 1373.452393
            hint_ratio_x = 0.954449
        elseif is_contract then
            verify_pattern = "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.UITitleItem.WidgetTree.BackBtn"
            label = "按O打开契灵面板"
            key_vk = 79
            hint_client_x = 23.411688
            hint_client_y = 39.000000
            hint_ratio_x = 0.016269
            hint_ratio_y = 0.043333
        end
        return {
            kind = "ensure_panel_open",
            key = key ~= "" and key or (is_talent and "open_talent_panel" or (is_contract and "open_contract_panel" or "open_skill_panel")),
            label = label,
            key_vk = key_vk,
            include_patterns = {
                verify_pattern
            },
            hint_client_x = hint_client_x,
            hint_client_y = hint_client_y,
            hint_ratio_x = hint_ratio_x,
            hint_ratio_y = hint_ratio_y,
            hint_max_distance = 120,
            target_poll_count = 0,
            target_poll_interval_ms = 100,
            open_wait_ms = 180,
            verify_timeout_ms = 3000,
            verify_poll_ms = 100,
            wait_after_ms = math.max(tonumber(source_step.wait_after_ms) or 0, 180)
        }
    end

    local function retarget_panel_open_steps(plans, panel_kind)
        if type(plans) ~= "table" then
            return
        end
        for _, plan in pairs(plans) do
            local source_steps = type(plan) == "table" and type(plan.steps) == "table" and plan.steps or nil
            if source_steps ~= nil then
                local steps = {}
                for _, step in ipairs(source_steps) do
                    local key = tostring(type(step) == "table" and step.key or "")
                    if panel_kind == "talent" and key:find("open_fast_entrance_menu", 1, true) ~= nil then
                        -- P opens the talent panel directly.
                    elseif panel_kind == "talent" and key:find("open_talent_panel", 1, true) ~= nil then
                        steps[#steps + 1] = make_hotkey_panel_open_step(step, "talent")
                    elseif panel_kind == "skill" and key:find("open_fast_entrance_menu", 1, true) ~= nil then
                        -- K opens the skill panel directly.
                    elseif panel_kind == "contract" and key:find("contract_open_menu", 1, true) ~= nil then
                        -- O opens the contract panel directly.
                    elseif panel_kind == "contract" and key:find("contract_open_panel", 1, true) ~= nil then
                        steps[#steps + 1] = make_hotkey_panel_open_step(step, "contract")
                    elseif panel_kind == "skill" and key:find("open_skill_panel", 1, true) ~= nil then
                        steps[#steps + 1] = make_hotkey_panel_open_step(step, "skill")
                    else
                        steps[#steps + 1] = step
                    end
                end
                plan.steps = steps
            end
        end
    end

    retarget_panel_open_steps(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_extra_by_level, "skill")
    retarget_panel_open_steps(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level, "skill")
    retarget_panel_open_steps(M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level, "talent")
    retarget_panel_open_steps(M.LEVEL_UP_MAINTENANCE_CONFIG.contract_by_level, "contract")
end

local function make_sun_faction_choice_action()
    local is_moon = SUN_FACTION_CHOICE == 2
    local faction_name = is_moon and "皓月" or "繁星"
    local point = is_moon
        and { x = 10357.46, y = 53.56, z = 501.00 }
        or { x = 10412.35, y = 1060.25, z = 501.00 }

    return make_npc_dialogue_route_action({
        key = is_moon and "daylight_rivalry_moon_faction_dialogue" or "daylight_rivalry_star_faction_dialogue",
        label = "与日争辉_选择" .. faction_name .. "阵营",
        retry_ms = 600000,
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "与英灵对话",
            "选择加入繁星或皓月阵营"
        },
        constraint_mode = "all",
        trigger = {
            x = 10442.29,
            y = 653.28,
            z = 501.00,
            radius = 520,
            z_tolerance = 260
        },
        dialogue = {
            x = point.x,
            y = point.y,
            z = point.z,
            radius = 260,
            interact_radius = 140,
            move_interval_ms = 220,
            z_tolerance = 260,
            center_settle_ms = 600,
            interact_retry_ms = 1800,
            timeout_ms = 20000,
            npc_search_radius = 520,
            fallback_interact = true
        }
    })
end

local function make_sun_faction_after_join_route_action()
    local is_moon = SUN_FACTION_CHOICE == 2
    local point = is_moon
        and { x = 10357.46, y = 53.56, z = 501.00 }
        or { x = 10412.35, y = 1060.25, z = 501.00 }

    return make_route_point_action({
        key = "daylight_rivalry_after_faction_join_route_10517_684",
        label = "与日争辉_加入阵营后繁星斗场长路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "挑战太阳斗场的英灵"
        },
        constraint_mode = "all",
        trigger = {
            x = point.x,
            y = point.y,
            z = point.z,
            radius = 650,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 240,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        route_worker_max_points = 80,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 10517.67, y = 684.98, z = 501.00 },
            { x = 11103.71, y = 613.97, z = 501.00 },
            { x = 12363.96, y = 587.90, z = 501.00 },
            { x = 13654.92, y = 574.84, z = 501.00 },
            { x = 14632.92, y = 600.94, z = 501.00 },
            { x = 15417.90, y = 606.74, z = 501.00 },
            { x = 16132.97, y = 612.49, z = 501.00 },
            { x = 17833.25, y = 758.36, z = 501.00 },
            { x = 18893.07, y = 503.72, z = 501.00 },
            { x = 19258.37, y = -110.06, z = 501.00 },
            { x = 20044.90, y = -118.83, z = 501.00 },
            { x = 20689.76, y = 62.81, z = 501.00 },
            { x = 20873.42, y = 684.56, z = 501.00 },
            { x = 20544.36, y = 1295.29, z = 501.00 },
            { x = 19887.11, y = 1465.94, z = 501.00 },
            { x = 19240.39, y = 1358.25, z = 501.00 },
            { x = 18816.40, y = 786.48, z = 501.00 },
            { x = 18717.57, y = 256.05, z = 501.00 },
            { x = 19243.14, y = 57.98, z = 501.00 },
            { x = 19894.75, y = -138.02, z = 501.00 },
            { x = 20472.34, y = -3.86, z = 501.00 },
            { x = 20729.59, y = 379.71, z = 501.00 },
            { x = 20779.03, y = 943.74, z = 501.00 },
            { x = 20494.52, y = 1272.72, z = 501.00 },
            { x = 20035.06, y = 1459.76, z = 501.00 },
            { x = 19461.40, y = 1366.22, z = 501.00 },
            { x = 19085.06, y = 986.92, z = 501.00 },
            { x = 18896.72, y = 551.56, z = 501.00 },
            { x = 19227.39, y = 80.48, z = 501.00 },
            { x = 19677.03, y = -125.15, z = 501.00 },
            { x = 19904.96, y = -502.25, z = 501.00 },
            { x = 19870.48, y = -1161.00, z = 501.00 },
            { x = 19833.31, y = -1999.56, z = 501.00 },
            { x = 20163.51, y = -1857.70, z = 501.00 },
            { x = 20599.00, y = -1642.00, z = 501.00 },
            { x = 21154.88, y = -1584.48, z = 501.00 },
            { x = 21586.32, y = -1441.70, z = 501.00 },
            { x = 21871.17, y = -1216.59, z = 501.00 },
            { x = 22007.57, y = -1003.84, z = 501.00 },
            { x = 22208.08, y = -661.71, z = 501.00 },
            { x = 22302.35, y = -341.63, z = 501.00 },
            { x = 22337.23, y = -91.13, z = 501.00 },
            { x = 22370.06, y = 330.39, z = 501.00 },
            { x = 22365.86, y = 751.11, z = 501.00 },
            { x = 22349.88, y = 1199.52, z = 501.00 },
            { x = 22331.83, y = 1685.48, z = 501.00 },
            { x = 22309.09, y = 2146.50, z = 501.00 },
            { x = 22192.03, y = 2605.91, z = 501.00 },
            { x = 21871.37, y = 2896.17, z = 501.00 },
            { x = 21515.72, y = 2985.77, z = 501.00 },
            { x = 20952.14, y = 3021.57, z = 501.00 },
            { x = 20471.62, y = 3046.82, z = 501.00 },
            { x = 19989.70, y = 3182.52, z = 501.00 },
            { x = 19874.90, y = 3552.39, z = 501.00 },
            { x = 19824.50, y = 3888.29, z = 501.00 },
            { x = 19778.28, y = 4209.49, z = 501.00 },
            { x = 19747.36, y = 4572.76, z = 501.00 },
            { x = 19763.15, y = 5102.72, z = 501.00 },
            { x = 19778.91, y = 5574.56, z = 501.00 },
            { x = 19779.59, y = 6052.10, z = 501.00 },
            { x = 19771.31, y = 6476.11, z = 501.00 },
            { x = 19759.05, y = 6866.42, z = 501.00 },
            { x = 19748.32, y = 7234.78, z = 501.00 },
            { x = 19776.15, y = 7654.41, z = 501.00 },
            { x = 19805.24, y = 8045.22, z = 501.00 },
            { x = 19829.75, y = 8520.92, z = 501.00 },
            { x = 19850.82, y = 8956.09, z = 501.00 },
            { x = 19886.62, y = 9476.25, z = 501.00 }
        }
    })
end

local function make_sun_faction_no_path_recover_route_action()
    return make_route_point_action({
        key = "daylight_rivalry_join_faction_no_path_recover_route_10535_660",
        label = "与日争辉_选择阵营后无路径固定路线后重call主线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        wait_task_path_recover_only = true,
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "与英灵对话",
            "选择加入繁星或皓月阵营"
        },
        constraint_mode = "all",
        trigger = {
            x = 10535.00,
            y = 660.00,
            z = 501.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 150000,
        waypoint_reach_radius = 240,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        route_worker_max_points = 80,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 11275.23, y = 531.15, z = 501.00 },
            { x = 12000.29, y = 549.18, z = 501.00 },
            { x = 12571.04, y = 592.63, z = 501.00 },
            { x = 13576.84, y = 599.95, z = 501.00 },
            { x = 14267.84, y = 578.91, z = 501.00 },
            { x = 14945.09, y = 575.50, z = 501.00 },
            { x = 15568.70, y = 590.18, z = 501.00 },
            { x = 16170.78, y = 604.41, z = 501.00 },
            { x = 16745.70, y = 651.76, z = 501.00 },
            { x = 17407.39, y = 773.12, z = 501.00 },
            { x = 18027.54, y = 692.88, z = 501.00 },
            { x = 18634.81, y = 461.57, z = 501.00 },
            { x = 19217.56, y = 190.10, z = 501.00 },
            { x = 19649.18, y = -136.69, z = 501.00 },
            { x = 19857.05, y = -717.91, z = 501.00 },
            { x = 19971.44, y = -1372.03, z = 501.00 },
            { x = 20223.55, y = -1656.49, z = 501.00 },
            { x = 20861.26, y = -1665.68, z = 501.00 },
            { x = 21602.59, y = -1534.13, z = 501.00 },
            { x = 22004.75, y = -1197.44, z = 501.00 },
            { x = 22119.27, y = -527.76, z = 501.00 },
            { x = 22291.34, y = 98.64, z = 501.00 },
            { x = 22379.01, y = 635.19, z = 501.00 },
            { x = 22347.01, y = 1167.51, z = 501.00 },
            { x = 22308.27, y = 1768.19, z = 501.00 },
            { x = 22298.63, y = 2386.58, z = 501.00 },
            { x = 22121.23, y = 2821.39, z = 501.00 },
            { x = 21458.59, y = 2918.05, z = 501.00 },
            { x = 20764.04, y = 2946.69, z = 501.00 },
            { x = 20159.45, y = 3035.30, z = 501.00 },
            { x = 19634.49, y = 3413.17, z = 501.00 },
            { x = 19592.93, y = 4039.61, z = 501.00 },
            { x = 19693.56, y = 4559.10, z = 501.00 },
            { x = 19717.76, y = 5137.32, z = 501.00 },
            { x = 19728.65, y = 5379.02, z = 501.00 },
            { x = 19749.26, y = 5724.12, z = 501.00 },
            { x = 19787.39, y = 6303.33, z = 501.00 },
            { x = 19931.00, y = 8692.00, z = 501.00 }
        }
    })
end

local function make_daylight_rivalry_arena_hero_route_action()
    return make_route_point_action({
        key = "daylight_rivalry_arena_hero_route_19913_9021",
        label = "与日争辉_挑战太阳斗场英灵_录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "挑战太阳斗场的英灵"
        },
        constraint_mode = "all",
        trigger = {
            x = 19913.10,
            y = 9020.56,
            z = 501.00,
            radius = 900,
            z_tolerance = 320
        },
        retry_ms = 600000,
        timeout_ms = 240000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 19441.69, y = 9863.42, z = 501.00 },
            { x = 18856.69, y = 10518.24, z = 501.00 },
            { x = 18731.74, y = 11021.06, z = 501.82 },
            { x = 18810.32, y = 11788.73, z = 506.00 },
            { x = 18587.39, y = 12318.10, z = 506.00 },
            { x = 18611.39, y = 12664.63, z = 506.00 },
            { x = 18941.91, y = 13069.50, z = 506.00 },
            { x = 19075.58, y = 13522.12, z = 506.61 },
            { x = 19169.99, y = 14090.89, z = 501.00 },
            { x = 19435.86, y = 14580.32, z = 501.00 },
            { x = 19912.13, y = 14371.56, z = 501.00 },
            { x = 20256.43, y = 14003.76, z = 501.00 },
            { x = 20439.70, y = 13550.73, z = 508.54 },
            { x = 20611.35, y = 13144.26, z = 507.76 },
            { x = 20630.08, y = 12715.44, z = 506.00 },
            { x = 20609.31, y = 12299.90, z = 506.00 },
            { x = 20337.54, y = 11845.73, z = 507.82 },
            { x = 20130.69, y = 11680.80, z = 509.00 },
            { x = 19901.22, y = 11521.85, z = 510.94 },
            { x = 19694.71, y = 11377.16, z = 508.54 },
            { x = 19235.95, y = 11248.80, z = 506.00 },
            { x = 19016.96, y = 11526.75, z = 510.79 },
            { x = 18962.31, y = 11906.16, z = 506.54 },
            { x = 18911.15, y = 12417.37, z = 506.00 },
            { x = 19038.97, y = 12892.02, z = 506.00 },
            { x = 19349.40, y = 13164.34, z = 508.00 },
            { x = 19796.75, y = 13171.99, z = 507.81 },
            { x = 20105.78, y = 12874.07, z = 506.00 },
            { x = 20313.70, y = 12477.28, z = 506.00 },
            { x = 20254.56, y = 12038.74, z = 506.00 },
            { x = 19846.30, y = 11862.98, z = 507.28 },
            { x = 19433.73, y = 11956.38, z = 506.36 },
            { x = 19455.37, y = 12292.04, z = 506.00 },
            { x = 19788.01, y = 12486.47, z = 506.00 },
            { x = 20137.05, y = 12655.25, z = 506.00 },
            { x = 20359.50, y = 12734.85, z = 506.00 },
            { x = 20563.46, y = 12722.45, z = 506.00 },
            { x = 20745.02, y = 12573.20, z = 506.00 },
            { x = 20845.88, y = 12242.09, z = 506.00 },
            { x = 20746.14, y = 11969.43, z = 506.00 },
            { x = 20543.54, y = 11805.68, z = 508.00 },
            { x = 19936.75, y = 11670.83, z = 509.29 },
            { x = 20046.05, y = 12103.22, z = 506.00 },
            { x = 20476.26, y = 12165.54, z = 506.00 }
        }
    })
end

local function make_daylight_rivalry_baptism_anchor_route_action()
    return make_route_point_action({
        key = "daylight_rivalry_baptism_anchor_route_30389_11366",
        label = "与日争辉_接受圣洗_锚点路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "接受圣洗"
        },
        constraint_mode = "all",
        trigger = {
            x = 30389.00,
            y = 11366.00,
            z = 505.19,
            radius = 1800,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 45000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 30389.00, y = 11366.00, z = 505.19 }
        }
    })
end

local function make_daylight_rivalry_grand_arena_kite_task_config()
    return make_boss_kite_task_config(
        "daylight_rivalry_grand_arena_kite",
        {
            trigger_distance = 1800,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            no_task_target_kite_wait_ms = 900,
            seamless_kite = true,
            kite_point_count = 4,
            kite_switch_ms = 1800,
            kite_arrive_distance = 260,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            kite_points = {
                { x = 44712.00, y = 12618.58, z = 406.00 },
                { x = 45814.29, y = 12799.64, z = 406.00 },
                { x = 45936.27, y = 11663.12, z = 406.00 },
                { x = 44799.48, y = 11625.30, z = 406.00 }
            }
        },
        {
            task_patterns = {
                "与日争辉"
            },
            task_detail_patterns = {
                "挑战大竞技场",
                "击败“太阳冠军”杰拉尔德"
            },
            constraint_mode = "all"
        }
    )
end

local function make_daylight_rivalry_audience_queen_no_path_route_action()
    return make_route_point_action({
        key = "daylight_rivalry_audience_queen_no_path_route_to_npc",
        label = "与日争辉_觐见女王_无路径固定路线到NPC",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        wait_task_path_recover_only = true,
        direct_without_task_target_only = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "daylight_rivalry_audience_queen_npc_dialogue_54124_12029",
        followup_route_action_ignore_retry = true,
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "觐见女王"
        },
        constraint_mode = "all",
        trigger = {
            x = 47491.00,
            y = 12143.00,
            z = 406.00,
            radius = 1800,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 90000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        route_worker_max_points = 21,
        waypoints = {
            { x = 48726.38, y = 12049.30, z = 406.00 },
            { x = 50089.95, y = 12111.86, z = 407.06 },
            { x = 51303.48, y = 12187.30, z = 506.00 },
            { x = 53223.34, y = 12193.07, z = 509.00 },
            { x = 54288.97, y = 12108.20, z = 802.29 },
            { x = 55128.09, y = 12411.46, z = 801.00 },
            { x = 55201.17, y = 12053.92, z = 803.00 },
            { x = 54988.87, y = 12136.02, z = 801.00 },
            { x = 54960.44, y = 12676.47, z = 801.00 },
            { x = 54932.18, y = 12401.29, z = 801.00 },
            { x = 54926.09, y = 11901.12, z = 801.51 },
            { x = 55145.26, y = 12122.41, z = 802.00 },
            { x = 55248.57, y = 12415.22, z = 802.00 },
            { x = 55202.02, y = 12459.79, z = 801.38 },
            { x = 55704.50, y = 12430.73, z = 844.89 },
            { x = 56104.03, y = 12313.20, z = 976.66 },
            { x = 56192.63, y = 12002.30, z = 1001.90 },
            { x = 55726.41, y = 11930.15, z = 854.38 },
            { x = 55265.86, y = 11957.67, z = 803.06 },
            { x = 54696.83, y = 12002.02, z = 804.09 },
            { x = 54124.00, y = 12029.00, z = 789.14 }
        }
    })
end

local function make_daylight_rivalry_audience_queen_dialogue_route_action()
    return make_npc_dialogue_route_action({
        key = "daylight_rivalry_audience_queen_npc_dialogue_54124_12029",
        label = "与日争辉_觐见女王_固定路线后NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "觐见女王"
        },
        constraint_mode = "all",
        trigger = {
            x = 54124.00,
            y = 12029.00,
            z = 789.14,
            radius = 900,
            z_tolerance = 420
        },
        retry_ms = 600000,
        dialogue = {
            x = 54124.00,
            y = 12029.00,
            z = 789.14,
            radius = 360,
            interact_radius = 180,
            move_interval_ms = 220,
            z_tolerance = 420,
            center_settle_ms = 700,
            interact_retry_ms = 1600,
            timeout_ms = 22000,
            npc_search_radius = 900,
            fallback_interact = true
        }
    })
end

local function make_daylight_rivalry_talk_to_ariya_route_action()
    return make_npc_dialogue_route_action({
        key = "daylight_rivalry_talk_to_ariya_54146_12196",
        label = "与日争辉_与阿瑞娅交谈_NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "与阿瑞娅交谈"
        },
        constraint_mode = "all",
        trigger = {
            x = 54146.00,
            y = 12196.00,
            z = 801.88,
            radius = 1800,
            z_tolerance = 360
        },
        retry_ms = 600000,
        dialogue = {
            x = 54146.00,
            y = 12196.00,
            z = 801.88,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 220,
            z_tolerance = 360,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    })
end

local function make_evil_sun_chase_queen_detour_route_action()
    return make_route_point_action({
        key = "evil_sun_chase_queen_detour_route_4960_7571",
        label = "邪阳_追击太阳女王_录制路线到Boss",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "邪阳"
        },
        task_detail_patterns = {
            "追击太阳女王，拯救阿瑞娅"
        },
        constraint_mode = "all",
        trigger = {
            x = 4960.00,
            y = 7571.00,
            z = 1206.00,
            radius = 1800,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 120000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 3220.24, y = 7305.44, z = 1206.00 },
            { x = 2515.97, y = 7236.62, z = 1206.00 },
            { x = 1938.25, y = 7653.28, z = 1206.00 },
            { x = 1458.72, y = 7917.83, z = 1173.04 },
            { x = 873.07, y = 7853.87, z = 928.73 },
            { x = 387.49, y = 7610.83, z = 913.00 },
            { x = 421.16, y = 6668.07, z = 744.04 },
            { x = 207.13, y = 5867.46, z = 606.00 },
            { x = -386.02, y = 5694.50, z = 606.00 },
            { x = -901.37, y = 5904.29, z = 606.00 },
            { x = -1145.84, y = 6724.12, z = 596.58 },
            { x = -898.79, y = 7205.73, z = 606.00 },
            { x = -1007.26, y = 7877.79, z = 604.48 },
            { x = -1183.46, y = 8218.01, z = 606.00 },
            { x = -1326.65, y = 8404.01, z = 606.00 },
            { x = -1568.83, y = 8946.37, z = 606.00 },
            { x = -1243.46, y = 9605.11, z = 606.00 },
            { x = -571.96, y = 9730.78, z = 606.00 },
            { x = 172.86, y = 9841.78, z = 606.00 },
            { x = 950.42, y = 9945.04, z = 606.00 },
            { x = 1818.80, y = 10347.91, z = 606.00 }
        }
    })
end

local function make_shadow_sun_chase_queen_detour_route_action()
    return make_route_point_action({
        key = "shadow_sun_chase_queen_detour_route_3263_-628",
        label = "恶影拜日_追踪太阳女王_录制路线后重call主线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "恶影拜日"
        },
        task_detail_patterns = {
            "追踪太阳女王，解救阿瑞娅"
        },
        constraint_mode = "all",
        trigger = {
            x = 3263.00,
            y = -628.00,
            z = 606.00,
            radius = 900,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 60000,
        waypoint_reach_radius = 240,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 2724.04, y = 748.37, z = 611.61 },
            { x = 2469.00, y = 1434.20, z = 606.00 },
            { x = 2586.37, y = 2262.41, z = 606.00 },
            { x = 2317.48, y = 2949.40, z = 606.00 },
            { x = 1757.68, y = 3403.84, z = 606.00 }
        }
    })
end

local function make_sun_faction_join_dialogue_flow_task()
    local is_moon = SUN_FACTION_CHOICE == 2
    local faction_name = is_moon and "皓月" or "繁星"
    local anchor_text = "加入" .. faction_name .. "英灵"

    return make_dialogue_locator_flow_task_config(
        is_moon and "daylight_rivalry_join_moon_faction_flow" or "daylight_rivalry_join_star_faction_flow",
        {
            {
                key = is_moon and "daylight_rivalry_join_moon_faction_btn" or "daylight_rivalry_join_star_faction_btn",
                label = anchor_text .. "按钮",
                distance_anchor_exact_text = anchor_text,
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 61.059764,
                distance_max = 64.836656,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 609.029968,
                hint_client_y = 348.017120,
                hint_ratio_x = 0.422937,
                hint_ratio_y = 0.386686,
                hint_max_distance = 120.000,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 609.029968,
                fixed_fallback_client_y = 348.017120,
                fixed_fallback_ratio_x = 0.422937,
                fixed_fallback_ratio_y = 0.386686,
                fixed_fallback_mouse_mode = "api",
                retry_ms = 600,
                settle_ms = 1200
            }
        },
        {
            key = is_moon and "daylight_rivalry_join_moon_faction_flow" or "daylight_rivalry_join_star_faction_flow",
            timeout_ms = 9000,
            origins = {
                "npc",
                "interaction_prompt"
            },
            settle_ms = 1200
        },
        {
            task_patterns = {
                "与日争辉"
            },
            task_detail_patterns = {
                "与英灵对话",
                "选择加入繁星或皓月阵营"
            },
            constraint_mode = "all",
            allow_wait_task_path_route_action_recover = true,
            post_dialogue_flow = {
                key = is_moon and "daylight_rivalry_join_moon_wait_after_jump"
                    or "daylight_rivalry_join_star_wait_after_jump",
                wait_task_info_refresh_after_jump = true,
                task_info_refresh_timeout_ms = 12000
            }
        }
    )
end

local function trial_of_sun_side_key_from_prefix(prefix)
    prefix = tostring(prefix or "")
    if prefix:find("power", 1, true) then
        return TRIAL_OF_SUN_POWER_SIDE_KEY
    end
    if prefix:find("conquest", 1, true) then
        return TRIAL_OF_SUN_CONQUEST_SIDE_KEY
    end
    if prefix:find("beauty", 1, true) then
        return TRIAL_OF_SUN_BEAUTY_SIDE_KEY
    end
    return nil
end

local function mark_trial_of_sun_action(action, side_task_key, opts)
    if type(action) ~= "table" then
        return action
    end
    opts = type(opts) == "table" and opts or {}
    local action_key = tostring(action.key or action.label or opts.step_key or "")
    action.recipe_package_key = TRIAL_OF_SUN_THREE_TRIALS_PACKAGE_KEY
    action.recipe_side_task_key = side_task_key
    action.recipe_step_key = tostring(opts.step_key or action_key)
    action.recipe_next_action_key = opts.next_action_key or action.followup_route_action_key or action.next_route_point_action_key
    if opts.complete_side_task == true then
        action.recipe_complete_side_task_key = side_task_key
    end
    return action
end

local function make_trial_of_sun_recorded_route_action(side_task_key, opts)
    opts = type(opts) == "table" and opts or {}
    local action = make_route_point_action(opts)
    return mark_trial_of_sun_action(action, side_task_key, {
        step_key = opts.recipe_step_key or action.key,
        next_action_key = opts.followup_route_action_key or opts.next_route_point_action_key,
        complete_side_task = opts.recipe_complete_side_task == true
    })
end

local function make_trial_of_sun_power_maptrap_step(index)
    return {
        key = string.format("trial_of_sun_power_maptrap_btn_%02d", tonumber(index) or 0),
        label = string.format("权力之试MapTrapBtn_%02d", tonumber(index) or 0),
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
        },
        hint_client_x = 704.528564,
        hint_client_y = 712.631531,
        hint_ratio_x = 0.489256,
        hint_ratio_y = 0.791813,
        hint_max_distance = 110.000,
        prefer_hint_fallback = true,
        settle_ms = 900,
        retry_ms = 1200
    }
end

local function make_trial_of_sun_power_maptrap_action(index, from_point, objective_point, followup_key)
    local action_key = string.format("trial_of_sun_power_maptrap_%02d", tonumber(index) or 0)
    local action = make_route_point_action({
        key = action_key,
        label = string.format("太阳的试炼_权力之试_MapTrap_%02d", tonumber(index) or 0),
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "权力之试",
            "通过权力试炼",
            "太阳的试炼",
            "通过三处试炼"
        },
        task_detail_patterns = {
            "通过权力试炼",
            "权力之瞳",
            "通过三处试炼",
            "追击夺走火种"
        },
        trigger = {
            x = tonumber(from_point and from_point.x),
            y = tonumber(from_point and from_point.y),
            z = tonumber(from_point and from_point.z),
            radius = tonumber(from_point and from_point.radius) or 1100,
            z_tolerance = tonumber(from_point and from_point.z_tolerance) or 260
        },
        objective_point = {
            x = tonumber(objective_point and objective_point.x),
            y = tonumber(objective_point and objective_point.y),
            z = tonumber(objective_point and objective_point.z),
            radius = tonumber(objective_point and objective_point.radius) or 260,
            z_tolerance = tonumber(objective_point and objective_point.z_tolerance) or 260
        },
        interact_radius = tonumber(objective_point and objective_point.interact_radius) or 220,
        probe_retry_ms = 650,
        retry_ms = 600000,
        settle_ms = 1200,
        timeout_ms = 35000,
        force_task_call_after_transition = false,
        skip_missing_button_to_followup = followup_key ~= nil,
        missing_button_complete_side_task = followup_key == nil,
        missing_button_skip_after_ms = 3000,
        followup_route_action_key = followup_key,
        step = make_trial_of_sun_power_maptrap_step(index)
    })
    return mark_trial_of_sun_action(action, TRIAL_OF_SUN_POWER_SIDE_KEY, {
        step_key = action_key,
        next_action_key = followup_key,
        complete_side_task = followup_key == nil
    })
end

local function make_trial_of_sun_side_maptrap_step(prefix, label, index)
    return {
        key = string.format("%s_maptrap_btn_%02d", tostring(prefix or "trial_of_sun_side"), tonumber(index) or 0),
        label = string.format("%sMapTrapBtn_%02d", tostring(label or "试炼"), tonumber(index) or 0),
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
        },
        hint_client_x = 704.528564,
        hint_client_y = 712.631531,
        hint_ratio_x = 0.489256,
        hint_ratio_y = 0.791813,
        hint_max_distance = 110.000,
        prefer_hint_fallback = true,
        settle_ms = 900,
        retry_ms = 1200
    }
end

local function make_trial_of_sun_side_maptrap_action(opts)
    opts = type(opts) == "table" and opts or {}
    local index = tonumber(opts.index) or 0
    local prefix = tostring(opts.prefix or "trial_of_sun_side")
    local from_point = type(opts.from_point) == "table" and opts.from_point or {}
    local objective_point = type(opts.objective_point) == "table" and opts.objective_point or from_point
    local action = make_route_point_action({
        key = string.format("%s_maptrap_%02d", prefix, index),
        label = string.format("%s_MapTrap_%02d", tostring(opts.label or "太阳的试炼"), index),
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = opts.task_patterns,
        task_detail_patterns = opts.task_detail_patterns,
        trigger = {
            x = tonumber(from_point.x),
            y = tonumber(from_point.y),
            z = tonumber(from_point.z),
            radius = tonumber(from_point.radius) or 1100,
            z_tolerance = tonumber(from_point.z_tolerance) or 260
        },
        objective_point = {
            x = tonumber(objective_point.x),
            y = tonumber(objective_point.y),
            z = tonumber(objective_point.z),
            radius = tonumber(objective_point.radius) or 260,
            z_tolerance = tonumber(objective_point.z_tolerance) or 260
        },
        interact_radius = tonumber(objective_point.interact_radius) or 220,
        probe_retry_ms = tonumber(opts.probe_retry_ms) or 650,
        retry_ms = tonumber(opts.retry_ms) or 600000,
        settle_ms = tonumber(opts.settle_ms) or 1200,
        timeout_ms = tonumber(opts.timeout_ms) or 35000,
        force_task_call_after_transition = opts.force_task_call_after_transition == true,
        skip_missing_button_to_followup = opts.followup_route_action_key ~= nil,
        missing_button_complete_side_task = opts.followup_route_action_key == nil,
        missing_button_skip_after_ms = tonumber(opts.missing_button_skip_after_ms) or 3000,
        followup_route_action_key = opts.followup_route_action_key,
        combat_pulse_while_waiting = opts.combat_pulse_while_waiting == true,
        step = make_trial_of_sun_side_maptrap_step(prefix, opts.button_label or opts.label, index)
    })
    return mark_trial_of_sun_action(action, opts.side_task_key or trial_of_sun_side_key_from_prefix(prefix), {
        step_key = action.key,
        next_action_key = opts.followup_route_action_key,
        complete_side_task = opts.followup_route_action_key == nil
    })
end

M.ENABLE_MAP_RUNTIME_DETECTION = false
M.LEVELING_USE_NAV_WORKER = true
M.LEVELING_USE_COMBAT_WORKER = true
M.COMBAT_WORKER_LEASE_MS = 4000
-- Keep the legacy state machine authoritative. BT wrappers are opt-in for local
-- execution groups only, so testing does not depend on scheduler ownership.
M.LEVELING_BT_EXECUTION_ENABLED = false
M.LEVELING_BT_OWNER_GATE_ENFORCE = false
M.LEVELING_BT_ACTIVE_OWNER_ENFORCE = false
M.TASK_REFRESH_BAG_PAUSE_ENABLED = true
M.TASK_REFRESH_BAG_PAUSE_KEY_VK = 0x42
M.TASK_REFRESH_BAG_PAUSE_VERIFY_ATTEMPTS = 5
M.TASK_REFRESH_BAG_PAUSE_VERIFY_INTERVAL_MS = 100
M.TASK_REFRESH_BAG_PAUSE_RETRY_MS = 900
M.TASK_REFRESH_BAG_PAUSE_MAX_MS = 12000
M.TASK_PATH_WORKER_ROUTE_MODE = true
M.TASK_PATH_USE_RAW_PATH = true
M.TASK_PATH_WORKER_MAX_POINTS = 1024
M.TASK_FOLLOW_MOVE_INTERVAL_MS = 1200
M.TASK_POS_MOVE_INTERVAL_MS = 900
M.TASK_COMBAT_KITE_ASYNC_ROUTE_WORKER = true

local TREASURE_DUNGEON_BASELINE_DEFAULTS = {
    inside_detect_task_panel_text = false,
    startup_recovery_wait_for_task_panel = true,
    startup_recovery_activate_by_level_gate = true,
    startup_recovery_requires_task_match = false,
    startup_recovery_allow_task_mismatch_by_level_gate = true,
    startup_recovery_allow_inside_landing_task_mismatch_by_level_gate = true,
    startup_recovery_allow_landing_task_mismatch_by_level_gate = true,
    startup_recovery_allow_route_nearby_task_mismatch_by_level_gate = true,
    startup_recovery_map_token_task_mismatch_requires_route_nearby = true,
    startup_recovery_task_panel_wait_ms = 1800,
    startup_recovery_task_panel_wait_cap_ms = 9000,
    startup_recovery_route_nearby = true,
    startup_recovery_route_distance = 1800,
    resume_route_nearby = true,
    resume_route_distance = 1800,
    entry_far_reacquire_mainline = true,
    entry_far_reacquire_distance = 1800
}

local function apply_treasure_dungeon_baseline_defaults(configs)
    if type(configs) ~= "table" then
        return configs
    end
    for _, cfg in ipairs(configs) do
        if type(cfg) == "table" then
            for key, value in pairs(TREASURE_DUNGEON_BASELINE_DEFAULTS) do
                if cfg[key] == nil then
                    cfg[key] = value
                end
            end
        end
    end
    return configs
end

M.TREASURE_DUNGEON_CONFIGS = {
    {
        key = "treasure_milu_creek",
        enabled = true,
        name = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{871C}\u{9732}\u{6EAA}\u{8C37}",
        route_store_key = "treasure_milu_creek",
        target_level = 25,
        entry_activation_ignore_map_gate = true,
        inside_detect_task_panel_text = false,
        startup_recovery_wait_for_task_panel = true,
        startup_recovery_activate_by_level_gate = true,
        startup_recovery_requires_task_match = false,
        startup_recovery_allow_task_mismatch_by_level_gate = true,
        startup_recovery_allow_landing_task_mismatch_by_level_gate = true,
        startup_recovery_allow_route_nearby_task_mismatch_by_level_gate = true,
        startup_recovery_task_panel_wait_ms = 1800,
        startup_recovery_task_panel_wait_cap_ms = 9000,
        startup_recovery_route_nearby = true,
        startup_recovery_route_distance = 1800,
        resume_route_nearby = true,
        resume_route_distance = 1800,
        entry_far_reacquire_mainline = true,
        entry_far_reacquire_distance = 1800,
        task_patterns = {
            "\u{71C3}\u{70E7}\u{7684}\u{957F}\u{591C}",
            "\u{85CF}\u{5B9D}\u{5730}",
            "\u{871C}\u{9732}\u{6EAA}\u{8C37}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{5F02}\u{5316}\u{7684}\u{4E3D}\u{8299}",
            "\u{901A}\u{5173}1\u{6B21}\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{871C}\u{9732}\u{6EAA}\u{8C37}"
        },
        map_patterns = {
            "\u{9634}\u{7FCE}\u{4E4B}\u{5730}",
            "\u{871C}\u{9732}\u{6EAA}\u{8C37}"
        },
        entry_trigger = {
            x = 4167.00,
            y = 3949.00,
            z = 1603.89,
            radius = 1800,
            z_tolerance = 260
        },
        entry_steps = {
            {
                key = "treasure_milu_creek_map_trap",
                label = "313/313\u{6309}\u{94AE}",
                direct_interact = true,
                direct_interact_retry_ms = 1600,
                distance_anchor_exact_text = "313/313",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                distance_min = 232.174194,
                distance_max = 237.174194,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                },
                hint_client_x = 699.204834,
                hint_client_y = 724.439941,
                hint_ratio_x = 0.485559,
                hint_ratio_y = 0.804933,
                hint_max_distance = 80.000,
                interact_distance = 340,
                settle_ms = 1500,
                retry_ms = 2500
            },
            {
                key = "treasure_milu_creek_send",
                label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
                trigger = {
                    x = 4227.00,
                    y = 3923.00,
                    z = 1594.09,
                    radius = 100,
                    z_tolerance = 260
                },
                interact_distance = 60,
                distance_anchor_exact_text = "\u{4F20}\u{9001}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
                distance_min = 8.248740,
                distance_max = 9.248740,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
                },
                hint_client_x = 661.188843,
                hint_client_y = 827.716736,
                hint_ratio_x = 0.459159,
                hint_ratio_y = 0.919685,
                hint_max_distance = 80.000,
                settle_ms = 1800,
                retry_ms = 2500
            }
        },
        entry_ui_retry_timeout_ms = 6500,
        panel_query = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{871C}\u{9732}\u{6EAA}\u{8C37}",
        panel_query_fallbacks = {
            "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{871C}\u{9732}\u{6EAA}\u{8C37}",
            "\u{871C}\u{9732}\u{6EAA}\u{8C37}",
            "\u{85CF}\u{5B9D}\u{5730}"
        },
        panel_button_step = {
            key = "treasure_milu_creek_task_button",
            label = "\u{652F}\u{7EBF} \u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{871C}\u{9732}\u{6EAA}\u{8C37}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{652F}\u{7EBF} \u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{871C}\u{9732}\u{6EAA}\u{8C37}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn",
            distance_anchor_required = true,
            distance_min = 28.000000,
            distance_max = 42.000000,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn"
            },
            hint_client_x = 83.907120,
            hint_client_y = 292.023743,
            hint_ratio_x = 0.058269,
            hint_ratio_y = 0.324471,
            hint_max_distance = 80.000
        },
        panel_button_step_required = true,
        path_retry_count = 5,
        path_retry_interval_ms = 1200,
        min_path_points = 3,
        acquire_path_hold_navigation = true,
        acquire_path_combat_sidecar = true,
        discard_terminal_route_nearby_resume = true,
        terminal_route_resume_distance = 450,
        route_refresh_ms = 900,
        route_arrive_tolerance = 150,
        route_reanchor_forward_delta = 8,
        nearby_monster_hard_hold_distance = 200,
        nearby_monster_soft_hold_distance = 350,
        nearby_monster_soft_hold_timeout_ms = 4000,
        loot_press_interval_ms = 700,
        loot_stuck_max_attempts = 2,
        loot_ignore_ms = 12000,
        route_simplify = {
            min_spacing = 240,
            z_keep_delta = 90,
            turn_cos_threshold = 0.9910
        },
        boss = {
            enabled = true,
            name_patterns = {
                "暗域壁垒"
            },
            loot_enabled = true,
            loot_anchor_distance = 320,
            reentry_trigger = {
                x = 2379.95,
                y = -991.42,
                z = 1162.00,
                radius = 900,
                z_tolerance = 420
            },
            trigger = {
                x = 1332.07,
                y = -3484.13,
                z = 1147.00,
                radius = 1200,
                z_tolerance = 420,
                use_route_destination = false
            },
            kite_points = {
                { x = 761.00,  y = -4165.00, z = 1147.00 },
                { x = 1937.00, y = -3761.00, z = 1147.00 },
                { x = 1794.00, y = -2726.00, z = 1147.00 },
                { x = 649.00,  y = -3102.00, z = 1147.00 }
            },
            kite_radius = 3200,
            pre_engage_anchor_distance = 220,
            zero_monster_grace_ms = 700,
            clear_settle_ms = 3200
        },
        portals = {
            restart = {
                key = "treasure_milu_creek_restart_portal",
                kind = "restart",
                trigger = {
                    x = 971.00,
                    y = -4255.00,
                    z = 1147.00,
                    radius = 320,
                    z_tolerance = 420
                },
                step = {
                    key = "treasure_milu_creek_restart_portal_btn",
                    label = "\u{6C42}\u{751F}\u{4E4B}\u{6B32}\u{6309}\u{94AE}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                    },
                    hint_client_x = 703.204834,
                    hint_client_y = 729.439941,
                    hint_ratio_x = 0.488337,
                    hint_ratio_y = 0.810489,
                    hint_max_distance = 80.000
                },
                interact_distance = 260,
                retry_ms = 1500,
                settle_ms = 5000,
                fallback_interact = true,
                direct_nearest_button = true
            },
            exit = {
                key = "treasure_milu_creek_exit_portal",
                kind = "exit",
                trigger = {
                    x = 1832.87,
                    y = -4403.05,
                    z = 1147.00,
                    radius = 320,
                    z_tolerance = 420
                },
                step = {
                    key = "treasure_milu_creek_exit_portal_btn",
                    label = "\u{51FA}\u{56FE}\u{4F20}\u{9001}\u{95E8}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
                    },
                    hint_client_x = 705.204834,
                    hint_client_y = 726.439941,
                    hint_ratio_x = 0.489726,
                    hint_ratio_y = 0.807155,
                    hint_max_distance = 180.000
                },
                interact_distance = 260,
                retry_ms = 1500,
                settle_ms = 5000,
                fallback_interact = true,
                direct_nearest_button = true
            }
        },
        restart_landing = {
            x = 21250.00,
            y = 10260.00,
            z = 1147.00,
            radius = 2400,
            z_tolerance = 600
        },
        exit_landing = {
            x = 4167.00,
            y = 3949.00,
            z = 1603.89,
            radius = 2400,
            z_tolerance = 900
        },
        extra_exit_landings = {
            {
                x = 1433.78,
                y = -2558.69,
                z = 3141.51,
                radius = 2400,
                z_tolerance = 900
            }
        },
        transition_timeout_ms = 15000
    },
    {
        -- Based on the working treasure_milu_creek baseline, but this dungeon
        -- still needs real restart portal / restart landing verification.
        key = "treasure_empire_ashes_wolf_ambush_entry",
        enabled = true,
        name = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{66D9}\u{5149}\u{5927}\u{9053}",
        route_store_key = "treasure_empire_ashes_wolf_ambush_entry",
        target_level = 36,
        entry_far_reacquire_mainline = true,
        entry_far_reacquire_distance = 1800,
        inside_detect_task_panel_text = false,
        enter_detect_task_panel_query = false,
        startup_recovery_restart_landing = false,
        startup_recovery_wait_for_task_panel = true,
        startup_recovery_activate_by_level_gate = true,
        startup_recovery_allow_task_mismatch_by_level_gate = true,
        startup_recovery_allow_inside_landing_task_mismatch_by_level_gate = true,
        startup_recovery_task_panel_wait_ms = 1800,
        startup_recovery_task_panel_wait_cap_ms = 9000,
        startup_recovery_route_nearby = true,
        startup_recovery_route_distance = 1800,
        resume_route_nearby = true,
        resume_route_distance = 1800,
        task_patterns = {
            "\u{5E1D}\u{56FD}\u{4F59}\u{7130}"
        },
        task_detail_patterns = {
            "\u{7A81}\u{7834}\u{7FA4}\u{72FC}\u{5E2E}\u{4F0F}\u{51FB}",
            "\u{63A2}\u{7D22}\u{7FA4}\u{72FC}\u{8857}\u{5DF7}",
            "\u{6DF1}\u{5165}\u{7FA4}\u{72FC}\u{8857}\u{5DF7}",
            "\u{524D}\u{5F80}\u{7FA4}\u{72FC}\u{8857}\u{5DF7}"
        },
        entry_trigger = {
            x = -908.56,
            y = 9365.29,
            z = 606.00,
            radius = 500,
            z_tolerance = 260
        },
        entry_steps = {
            {
                key = "treasure_empire_ashes_wolf_ambush_map_trap",
                label = "313/313\u{6309}\u{94AE}",
                direct_interact = true,
                direct_interact_retry_ms = 1600,
                distance_anchor_exact_text = "313/313",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                distance_min = 232.174194,
                distance_max = 237.174194,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                },
                hint_client_x = 699.204834,
                hint_client_y = 724.439941,
                hint_ratio_x = 0.485559,
                hint_ratio_y = 0.804933,
                hint_max_distance = 80.000,
                settle_ms = 1500,
                retry_ms = 2500
            },
            {
                key = "treasure_empire_ashes_wolf_ambush_send",
                label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
                trigger = {
                    x = -908.56,
                    y = 9365.29,
                    z = 606.00,
                    radius = 500,
                    z_tolerance = 260
                },
                interact_distance = 60,
                distance_anchor_exact_text = "\u{4F20}\u{9001}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
                distance_min = 8.248740,
                distance_max = 9.248740,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
                },
                hint_client_x = 661.188843,
                hint_client_y = 827.716736,
                hint_ratio_x = 0.459159,
                hint_ratio_y = 0.919685,
                hint_max_distance = 80.000,
                settle_ms = 1800,
                retry_ms = 2500
            }
        },
        entry_ui_retry_timeout_ms = 6500,
        panel_query = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{66D9}\u{5149}\u{5927}\u{9053}",
        panel_query_fallbacks = {
            "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{66D9}\u{5149}\u{5927}\u{9053}",
            "\u{66D9}\u{5149}\u{5927}\u{9053}",
            "\u{85CF}\u{5B9D}\u{5730}",
            "\u{5E1D}\u{56FD}\u{4F59}\u{7130}",
            "\u{524D}\u{5F80}\u{7FA4}\u{72FC}\u{8857}\u{5DF7}",
            "\u{7FA4}\u{72FC}\u{8857}\u{5DF7}"
        },
        panel_button_step = {
            key = "treasure_empire_ashes_wolf_ambush_task_button",
            label = "\u{652F}\u{7EBF} \u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{66D9}\u{5149}\u{5927}\u{9053}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{652F}\u{7EBF} \u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{66D9}\u{5149}\u{5927}\u{9053}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn",
            distance_anchor_required = true,
            distance_min = 28.000000,
            distance_max = 42.000000,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn"
            },
            hint_client_x = 82.907120,
            hint_client_y = 290.023743,
            hint_ratio_x = 0.057574,
            hint_ratio_y = 0.322249,
            hint_max_distance = 80.000
        },
        panel_button_step_required = true,
        path_retry_count = 5,
        path_retry_interval_ms = 1200,
        min_path_points = 3,
        route_refresh_ms = 900,
        route_arrive_tolerance = 150,
        route_reanchor_forward_delta = 8,
        nearby_monster_hard_hold_distance = 200,
        nearby_monster_soft_hold_distance = 350,
        nearby_monster_soft_hold_timeout_ms = 4000,
        loot_press_interval_ms = 700,
        loot_stuck_max_attempts = 2,
        loot_ignore_ms = 12000,
        route_simplify = {
            min_spacing = 240,
            z_keep_delta = 90,
            turn_cos_threshold = 0.9910
        },
        fallback_route = {
            { x = 16800.00, y = -11300.00, z = 105.00 },
            { x = 16800.00, y = -9251.20, z = 105.00 },
            { x = 17360.00, y = -7490.02, z = 105.00 },
            { x = 15671.30, y = -7362.20, z = 105.00 },
            { x = 15547.85, y = -6176.87, z = 105.00 },
            { x = 17517.94, y = -6020.05, z = 105.00 },
            { x = 17905.04, y = -5160.10, z = 105.00 },
            { x = 15911.63, y = -5053.82, z = 105.00 },
            { x = 15026.05, y = -3978.68, z = 105.00 },
            { x = 16811.64, y = -3806.38, z = 105.00 },
            { x = 17902.57, y = -3275.27, z = 105.00 },
            { x = 18174.05, y = -1280.51, z = 105.00 },
            { x = 18807.25, y = -792.94, z = 105.00 },
            { x = 17086.03, y = -827.55, z = 105.00 },
            { x = 15145.59, y = -946.11, z = 105.00 },
            { x = 13137.38, y = -949.89, z = 105.00 },
            { x = 11152.17, y = -971.43, z = 105.00 },
            { x = 10121.01, y = -1814.29, z = 105.00 },
            { x = 10418.64, y = -3462.90, z = 105.00 },
            { x = 12066.74, y = -3595.10, z = 105.00 },
            { x = 11944.76, y = -5557.00, z = 105.00 },
            { x = 11583.36, y = -7138.03, z = 105.00 },
            { x = 10519.52, y = -6195.37, z = 105.00 },
            { x = 8956.45, y = -5668.55, z = 105.00 },
            { x = 6998.76, y = -5650.53, z = 105.00 },
            { x = 5670.73, y = -6033.38, z = 105.00 },
            { x = 5640.30, y = -7758.10, z = 105.00 },
            { x = 4791.34, y = -8925.27, z = 105.00 },
            { x = 4074.71, y = -7840.84, z = 105.00 },
            { x = 3850.62, y = -5864.77, z = 105.00 },
            { x = 3559.54, y = -3968.72, z = 105.00 },
            { x = 3883.73, y = -2482.03, z = 105.00 },
            { x = 5548.84, y = -2313.00, z = 105.00 },
            { x = 7089.97, y = -1640.37, z = 105.00 },
            { x = 7029.26, y = -53.35, z = 105.00 },
            { x = 5743.23, y = 858.67, z = 105.00 },
            { x = 5197.03, y = 2339.13, z = 5.00 },
            { x = 4856.94, y = 3763.78, z = 5.00 },
            { x = 3760.35, y = 2875.69, z = 5.00 },
            { x = 3748.34, y = 1057.88, z = 105.00 },
            { x = 2110.78, y = 894.49, z = 105.00 },
            { x = 517.57, y = 670.37, z = 105.00 },
            { x = -683.30, y = -533.20, z = 105.00 },
            { x = -327.00, y = 876.00, z = 105.00 },
            { x = 242.31, y = 2502.35, z = 105.00 },
            { x = -1598.59, y = 2693.41, z = 105.00 }
        },
        boss = {
            enabled = true,
            name_patterns = {
                "\u{6697}\u{57DF}\u{58C1}\u{5792}"
            },
            loot_enabled = true,
            loot_anchor_distance = 320,
            loot_max_pulses = 2,
            reentry_trigger = {
                x = 2379.95,
                y = -991.42,
                z = 1162.00,
                radius = 900,
                z_tolerance = 420
            },
            trigger = {
                x = 16744.00,
                y = -11139.00,
                z = 105.00,
                radius = 900,
                z_tolerance = 260,
                use_route_destination = false
            },
            kite_points = {
                { x = 16046.00, y = -11174.00, z = 105.00 },
                { x = 17429.50, y = -11163.34, z = 105.00 },
                { x = 16817.00, y = -10713.00, z = 105.00 }
            },
            kite_radius = 3200,
            pre_engage_anchor_distance = 220,
            zero_monster_grace_ms = 700,
            clear_settle_ms = 3200
        },
        portals = {
            restart = {
                key = "treasure_empire_ashes_wolf_ambush_restart_portal",
                kind = "restart",
                trigger = {
                    x = 17066.00,
                    y = -12015.00,
                    z = 105.00,
                    radius = 320,
                    z_tolerance = 260
                },
                step = {
                    key = "treasure_empire_ashes_wolf_ambush_restart_portal_btn",
                    label = "\u{6C42}\u{751F}\u{4E4B}\u{6B32}\u{6309}\u{94AE}",
                    distance_anchor_exact_text = "\u{6C42}\u{751F}\u{4E4B}\u{6B32}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                    distance_min = 243.257477,
                    distance_max = 248.257477,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                    },
                    hint_client_x = 703.204834,
                    hint_client_y = 729.439941,
                    hint_ratio_x = 0.488337,
                    hint_ratio_y = 0.810489,
                    hint_max_distance = 80.000,
                    prefer_hint_fallback = true
                },
                interact_distance = 260,
                retry_ms = 1500,
                settle_ms = 5000,
                fallback_interact = false,
                direct_nearest_button = true
            },
            exit = {
                key = "treasure_empire_ashes_wolf_ambush_exit_portal",
                kind = "exit",
                trigger = {
                    x = 16509.00,
                    y = -12043.00,
                    z = 105.00,
                    radius = 320,
                    z_tolerance = 260
                },
                step = {
                    key = "treasure_empire_ashes_wolf_ambush_exit_portal_btn",
                    label = "\u{51FA}\u{56FE}\u{4F20}\u{9001}\u{95E8}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
                    },
                    hint_client_x = 705.204834,
                    hint_client_y = 726.439941,
                    hint_ratio_x = 0.489726,
                    hint_ratio_y = 0.807155,
                    hint_max_distance = 180.000,
                    prefer_hint_fallback = true
                },
                interact_distance = 260,
                retry_ms = 1500,
                settle_ms = 5000,
                fallback_interact = true,
                direct_nearest_button = true
            }
        },
        inside_landing = {
            x = -1800.00,
            y = 2700.00,
            z = 105.00,
            radius = 900,
            z_tolerance = 260
        },
        extra_inside_landings = {
            {
                x = 10177.00,
                y = -3602.00,
                z = 105.00,
                radius = 1500,
                z_tolerance = 320
            },
            {
                x = 10332.00,
                y = -4312.00,
                z = 105.00,
                radius = 1500,
                z_tolerance = 320
            }
        },
        restart_landing = {
            x = -1800.00,
            y = 2700.00,
            z = 105.00,
            radius = 900,
            z_tolerance = 260
        },
        exit_landing = {
            x = -900.00,
            y = 9358.00,
            z = 606.00,
            radius = 2400,
            z_tolerance = 900
        },
        extra_exit_landings = {
            {
                x = -7064.00,
                y = -3717.00,
                z = 507.00,
                radius = 1800,
                z_tolerance = 900
            }
        },
        transition_timeout_ms = 15000,
        notes = {
            "Based on the proven treasure_milu_creek baseline, but restart portal / restart_landing are not yet verified for this dungeon",
            "Known task text: 帝国余焰 -> 前往群狼街巷",
            "Keep inside_detect_task_panel_text=false; outside detail may temporarily show 前往藏宝地：曙光大道 and must not skip entry flow",
            "Known entrance button behavior: same as previous treasure entrance button",
            "Verified inside boss anchor / kite points / exit portal from latest measured run; exit trigger moved to user F6 door anchor 16509,-12043,105 and restart trigger to 17066,-12015,105, but button F8 and real restart_landing still need verification",
            "Verified inside_landing after entry at -1800,2700,105; verified exit_landing near -827,9412,606; restart_landing still needs F7 after a real 求生之欲 restart click, so startup recovery must not use restart_landing yet",
            "Observed exit portal landing at -7064,-3717,507 after level-gated exit; accept it as extra_exit_landing to avoid retrying the inside exit portal after a successful transition",
            "Confirmed target_level=36 for return-to-mainline gate",
            "Restart/exit triggers are intentionally separated now; keep 16457.17,-12098.53 as exit-only data unless F7 proves otherwise",
            "Restart/exit portal probe now prefers hint fallback when distance-anchor locator drifts",
            "Before target_level=36 the restart portal must not fallback_interact; if the 求生之欲 MapTrapBtn cannot be located, wait for better button data instead of pressing E on the exit portal",
            "Final boss loot is intentionally capped at two pickup pulses",
            "Resume snapshot may restore from cached route proximity; this prevents a script restart inside the treasure route from falling back to mainline",
            "Startup recovery may also restore from cached route proximity when the visible task still matches this treasure's mainline task",
            "Verify boss name_patterns / panel_query and dedicated portal button locator logs on next real run"
        }
    },
    {
        -- Enabled for entry/route capture. Boss/portal/landing data is
        -- configured, but persisted route data is still empty until first run.
        key = "treasure_new_sprout_hill_entry",
        enabled = true,
        name = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{65B0}\u{7A57}\u{5C71}\u{4E18}",
        route_store_key = "treasure_new_sprout_hill_entry_v3",
        target_level = 14,
        inside_detect_task_panel_text = false,
        startup_recovery_wait_for_task_panel = true,
        startup_recovery_activate_by_level_gate = true,
        startup_recovery_requires_task_match = false,
        startup_recovery_allow_task_mismatch_by_level_gate = true,
        startup_recovery_allow_inside_landing_task_mismatch_by_level_gate = true,
        startup_recovery_allow_landing_task_mismatch_by_level_gate = true,
        discard_terminal_route_nearby_resume = true,
        terminal_route_fail_without_boss = true,
        startup_recovery_task_panel_wait_ms = 1800,
        startup_recovery_task_panel_wait_cap_ms = 9000,
        task_patterns = {
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}",
            "\u{85CF}\u{5B9D}\u{5730}",
            "\u{65B0}\u{7A57}\u{5C71}\u{4E18}"
        },
        task_detail_patterns = {
            "\u{5BFB}\u{627E}\u{77EE}\u{4EBA}\u{56FD}\u{5EA6}\u{5165}\u{53E3}",
            "\u{901A}\u{5173}1\u{6B21}\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{65B0}\u{7A57}\u{5C71}\u{4E18}"
        },
        map_patterns = {
            "\u{9F99}\u{9AA8}\u{5E73}\u{539F}",
            "\u{65B0}\u{7A57}\u{5C71}\u{4E18}"
        },
        inside_map_patterns = {
            "\u{65B0}\u{7A57}\u{5C71}\u{4E18}"
        },
        inside_landing = {
            x = -1700.43,
            y = 1793.72,
            z = 2857.77,
            radius = 1800,
            z_tolerance = 900
        },
        entry_trigger = {
            x = 13597.00,
            y = 15915.00,
            z = 5214.00,
            radius = 420,
            z_tolerance = 320
        },
        entry_steps = {
            {
                key = "treasure_new_sprout_hill_map_trap",
                label = "\u{65B0}\u{7A57}\u{5C71}\u{4E18}\u{5165}\u{53E3}MapTrapBtn",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                },
                hint_client_x = 693.053040,
                hint_client_y = 699.797729,
                hint_ratio_x = 0.481621,
                hint_ratio_y = 0.777553,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                settle_ms = 1500,
                retry_ms = 2500
            },
            {
                key = "treasure_new_sprout_hill_send",
                label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
                trigger = {
                    x = 13597.00,
                    y = 15915.00,
                    z = 5214.00,
                    radius = 180,
                    z_tolerance = 320
                },
                interact_distance = 60,
                distance_anchor_exact_text = "\u{4F20}\u{9001}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
                distance_min = 8.248740,
                distance_max = 9.248740,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
                },
                hint_client_x = 661.188843,
                hint_client_y = 827.716736,
                hint_ratio_x = 0.459159,
                hint_ratio_y = 0.919685,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                settle_ms = 1800,
                retry_ms = 2500
            }
        },
        entry_ui_retry_timeout_ms = 6500,
        enter_detect_task_panel_query = false,
        panel_query = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{65B0}\u{7A57}\u{5C71}\u{4E18}",
        panel_query_fallbacks = {
            "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{65B0}\u{7A57}\u{5C71}\u{4E18}",
            "\u{65B0}\u{7A57}\u{5C71}\u{4E18}",
            "\u{85CF}\u{5B9D}\u{5730}"
        },
        panel_button_step = {
            key = "treasure_new_sprout_hill_task_button",
            label = "\u{652F}\u{7EBF} \u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{65B0}\u{7A57}\u{5C71}\u{4E18}\u{6309}\u{94AE}",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn"
            },
            hint_client_x = 94.302498,
            hint_client_y = 334.674988,
            hint_ratio_x = 0.065488,
            hint_ratio_y = 0.371861,
            hint_max_distance = 30.000
        },
        panel_button_step_required = true,
        path_retry_count = 5,
        path_retry_interval_ms = 1200,
        min_path_points = 12,
        acquire_path_require_boss_trigger = true,
        route_refresh_ms = 900,
        route_arrive_tolerance = 150,
        route_reanchor_forward_delta = 8,
        nearby_monster_hard_hold_distance = 200,
        nearby_monster_soft_hold_distance = 350,
        nearby_monster_soft_hold_timeout_ms = 4000,
        loot_press_interval_ms = 350,
        loot_stuck_max_attempts = 8,
        loot_ignore_ms = 12000,
        route_simplify = {
            min_spacing = 240,
            z_keep_delta = 90,
            turn_cos_threshold = 0.9910
        },
        boss = {
            enabled = true,
            loot_enabled = true,
            loot_anchor_distance = 320,
            loot_anchor = {
                x = 7423.00,
                y = 9030.00,
                z = 4895.00,
                radius = 220,
                z_tolerance = 420
            },
            trigger = {
                x = 8294.91,
                y = 8952.48,
                z = 4895.00,
                radius = 1700,
                z_tolerance = 420,
                use_route_destination = false
            },
            kite_points = {
                { x = 8294.91, y = 8952.48, z = 4895.00 },
                { x = 7189.06, y = 9828.23, z = 4895.00 },
                { x = 6496.59, y = 8638.76, z = 4895.00 }
            },
            kite_radius = 2600,
            pre_engage_anchor_distance = 220,
            zero_monster_grace_ms = 700,
            clear_settle_ms = 3200,
            loot_empty_confirm_ms = 1400
        },
        portals = {
            restart = {
                key = "treasure_new_sprout_hill_restart_portal",
                kind = "restart",
                trigger = {
                    x = 7972.00,
                    y = 8291.00,
                    z = 4895.00,
                    radius = 320,
                    z_tolerance = 420
                },
                step = {
                    key = "treasure_new_sprout_hill_restart_portal_btn",
                    label = "\u{6C42}\u{751F}\u{4E4B}\u{6B32}\u{6309}\u{94AE}",
                    distance_anchor_exact_text = "\u{6C42}\u{751F}\u{4E4B}\u{6B32}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                    distance_min = 243.257477,
                    distance_max = 248.257477,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                    },
                    hint_client_x = 703.204834,
                    hint_client_y = 729.439941,
                    hint_ratio_x = 0.488337,
                    hint_ratio_y = 0.810489,
                    hint_max_distance = 80.000,
                    prefer_hint_fallback = true
                },
                interact_distance = 260,
                retry_ms = 1500,
                settle_ms = 5000,
                fallback_interact = true,
                direct_nearest_button = true
            },
            exit = {
                key = "treasure_new_sprout_hill_exit_portal",
                kind = "exit",
                trigger = {
                    x = 8300.00,
                    y = 8980.00,
                    z = 4895.00,
                    radius = 320,
                    z_tolerance = 420
                },
                step = {
                    key = "treasure_new_sprout_hill_exit_portal_btn",
                    label = "\u{51FA}\u{56FE}\u{4F20}\u{9001}\u{95E8}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
                    },
                    hint_client_x = 705.204834,
                    hint_client_y = 726.439941,
                    hint_ratio_x = 0.489726,
                    hint_ratio_y = 0.807155,
                    hint_max_distance = 180.000,
                    prefer_hint_fallback = true
                },
                interact_distance = 260,
                retry_ms = 1500,
                settle_ms = 5000,
                fallback_interact = true,
                direct_nearest_button = true
            }
        },
        restart_landing = {
            x = -1708.00,
            y = 746.00,
            z = 5929.00,
            radius = 2400,
            z_tolerance = 700
        },
        restart_landing_require_verified = true,
        exit_landing = {
            x = 13463.00,
            y = 15847.00,
            z = 5214.00,
            radius = 2200,
            z_tolerance = 900
        },
        portal_click_no_move_distance = 220,
        transition_timeout_ms = 15000,
        notes = {
            "Known mainline text: 龙陨之野 -> 寻找矮人国度入口",
            "Known side task text: 藏宝地：新穗山丘 -> 通关1次藏宝地：新穗山丘",
            "Known outside entrance: 13597,15915,5214 on 龙骨平原",
            "Entry UI steps temporarily reuse the proven treasure_milu_creek/empire entrance buttons",
            "Target level is 14; return-to-mainline gate is enabled",
            "Boss loot uses faster pickup pulses plus empty-list confirmation to avoid leaving drops behind",
            "Configured boss center/kite points, restart portal, exit portal, restart landing, and exit landing from measured F6/F7 data",
            "Exit landing updated after latest F6 sample to 13463,15847,5214; wait_exit should resume mainline from this outside point",
            "Persisted route_acquired is still false until this treasure completes its first path capture",
            "Restart landing updated to -1708,746,5929; restart clicks must verify this landing instead of accepting boss-anchor fallback",
            "Portal click success is now verified by position change; unchanged position falls back to D/retry before continuing",
            "Entering must not use panel_query detection because mainline 龙陨之野 can remain selected after SendBtn; use inside_landing/map signals and the explicit side-task button only",
            "Startup at restart_landing while below target_level must be treated as still inside this treasure, even if the visible task panel is still mainline",
            "Startup recovery intentionally allows task mismatch only for this treasure's measured inside/restart landing plus level gate",
            "Route cache bumped to v3 after v2 stored a short non-boss route; acquired routes must include the boss trigger before grinding"
        }
    },
    {
        -- Enabled for entry / route capture following the other treasure
        -- baselines. Boss / restart / exit / landing data still needs
        -- verification from later runs.
        key = "treasure_fourth_entry_5643_-530",
        enabled = true,
        name = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{9690}\u{4E16}\u{91D1}\u{9601}",
        route_store_key = "treasure_fourth_entry_5643_-530_v1",
        target_level = 42,
        inside_detect_task_panel_text = false,
        startup_recovery_wait_for_task_panel = true,
        startup_recovery_activate_by_level_gate = true,
        startup_recovery_allow_task_mismatch_by_level_gate = true,
        startup_recovery_allow_inside_landing_task_mismatch_by_level_gate = true,
        startup_recovery_allow_route_nearby_task_mismatch_by_level_gate = true,
        startup_recovery_route_nearby = true,
        startup_recovery_route_distance = 1800,
        startup_recovery_allow_landing_task_mismatch_by_level_gate = false,
        startup_recovery_restart_landing = false,
        require_exit_landing_before_return_mainline = true,
        entry_far_reacquire_mainline = true,
        entry_far_reacquire_distance = 1500,
        inside_detect_restart_landing = false,
        discard_terminal_route_nearby_resume = true,
        terminal_route_fail_without_boss = true,
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}",
            "\u{85CF}\u{5B9D}\u{5730}",
            "\u{9690}\u{4E16}\u{91D1}\u{9601}"
        },
        task_detail_patterns = {
            "\u{7EE7}\u{7EED}\u{8FFD}\u{5BFB}\u{83B1}\u{5B89}\u{7684}\u{8E2A}\u{8FF9}",
            "\u{901A}\u{5173}1\u{6B21}\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{9690}\u{4E16}\u{91D1}\u{9601}",
            "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{9690}\u{4E16}\u{91D1}\u{9601}"
        },
        inside_map_patterns = {
            "\u{9690}\u{4E16}\u{91D1}\u{9601}"
        },
        inside_landing = {
            x = -150.00,
            y = -200.00,
            z = 56.00,
            radius = 900,
            z_tolerance = 220
        },
        entry_trigger = {
            x = 5642.69,
            y = -530.40,
            z = 503.00,
            radius = 420,
            z_tolerance = 360
        },
        entry_steps = {
            {
                key = "treasure_fourth_entry_map_trap_placeholder",
                label = "\u{7B2C}\u{56DB}\u{85CF}\u{5B9D}\u{5730}\u{5165}\u{53E3}\u{6309}\u{94AE}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                },
                hint_client_x = 698.204834,
                hint_client_y = 727.439941,
                hint_ratio_x = 0.484864,
                hint_ratio_y = 0.808267,
                hint_max_distance = 160.000,
                prefer_hint_fallback = true,
                fallback_interact = true,
                fallback_interact_distance = 180,
                settle_ms = 1500,
                retry_ms = 2500
            },
            {
                key = "treasure_fourth_entry_send",
                label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
                trigger = {
                    x = 5642.69,
                    y = -530.40,
                    z = 503.00,
                    radius = 160,
                    z_tolerance = 360
                },
                interact_distance = 60,
                distance_anchor_exact_text = "\u{4F20}\u{9001}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
                distance_min = 8.248740,
                distance_max = 9.248740,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
                },
                hint_client_x = 661.188843,
                hint_client_y = 827.716736,
                hint_ratio_x = 0.459159,
                hint_ratio_y = 0.919685,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                settle_ms = 1800,
                retry_ms = 2500
            }
        },
        entry_ui_retry_timeout_ms = 6500,
        enter_detect_task_panel_query = false,
        panel_query = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{9690}\u{4E16}\u{91D1}\u{9601}",
        panel_query_fallbacks = {
            "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{9690}\u{4E16}\u{91D1}\u{9601}",
            "\u{9690}\u{4E16}\u{91D1}\u{9601}",
            "\u{85CF}\u{5B9D}\u{5730}"
        },
        panel_button_step = {
            key = "treasure_fourth_hidden_gold_task_button",
            label = "\u{652F}\u{7EBF} \u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{9690}\u{4E16}\u{91D1}\u{9601}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{652F}\u{7EBF} \u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{9690}\u{4E16}\u{91D1}\u{9601}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn",
            distance_anchor_required = true,
            distance_min = 28.000000,
            distance_max = 42.000000,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn"
            },
            hint_client_x = 85.907120,
            hint_client_y = 292.023743,
            hint_ratio_x = 0.059658,
            hint_ratio_y = 0.324471,
            hint_max_distance = 80.000
        },
        panel_button_step_required = true,
        path_retry_count = 5,
        path_retry_interval_ms = 1200,
        min_path_points = 12,
        acquire_path_reject_first_point = {
            x = 3050.00,
            y = 7050.00,
            z = 503.00,
            radius = 900,
            z_tolerance = 360
        },
        acquire_path_require_boss_trigger = true,
        route_refresh_ms = 900,
        route_arrive_tolerance = 150,
        route_reanchor_forward_delta = 8,
        nearby_monster_hard_hold_distance = 200,
        nearby_monster_soft_hold_distance = 350,
        nearby_monster_soft_hold_timeout_ms = 4000,
        loot_press_interval_ms = 350,
        loot_stuck_max_attempts = 8,
        loot_ignore_ms = 12000,
        route_simplify = {
            min_spacing = 240,
            z_keep_delta = 90,
            turn_cos_threshold = 0.9910
        },
        boss = {
            enabled = true,
            loot_enabled = true,
            loot_anchor_distance = 320,
            loot_anchor = {
                x = 10763.00,
                y = 18809.00,
                z = -664.00,
                radius = 320,
                z_tolerance = 260
            },
            trigger = {
                x = 10670.88,
                y = 18175.38,
                z = -664.00,
                radius = 1800,
                z_tolerance = 260,
                use_route_destination = false
            },
            kite_points = {
                { x = 11711.53, y = 19285.31, z = -664.00 },
                { x = 9807.94, y = 19361.28, z = -664.00 },
                { x = 10670.88, y = 18175.38, z = -664.00 }
            },
            kite_radius = 2800,
            pre_engage_anchor_distance = 220,
            zero_monster_grace_ms = 700,
            clear_settle_ms = 3200,
            loot_empty_confirm_ms = 1400
        },
        portals = {
            restart = {
                key = "treasure_fourth_restart_portal",
                kind = "restart",
                trigger = {
                    x = 11010.00,
                    y = 19761.00,
                    z = -664.00,
                    radius = 320,
                    z_tolerance = 260
                },
                step = {
                    key = "treasure_fourth_restart_portal_btn",
                    label = "\u{6C42}\u{751F}\u{4E4B}\u{6B32}\u{6309}\u{94AE}",
                    distance_anchor_exact_text = "\u{6C42}\u{751F}\u{4E4B}\u{6B32}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                    distance_min = 243.257477,
                    distance_max = 248.257477,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                    },
                    hint_client_x = 703.204834,
                    hint_client_y = 729.439941,
                    hint_ratio_x = 0.488337,
                    hint_ratio_y = 0.810489,
                    hint_max_distance = 80.000,
                    prefer_hint_fallback = true
                },
                interact_distance = 260,
                retry_ms = 1500,
                settle_ms = 5000,
                fallback_interact = false,
                direct_nearest_button = true
            },
            exit = {
                key = "treasure_fourth_exit_portal",
                kind = "exit",
                trigger = {
                    x = 10150.00,
                    y = 19750.00,
                    z = -664.00,
                    radius = 320,
                    z_tolerance = 260
                },
                step = {
                    key = "treasure_fourth_exit_portal_btn",
                    label = "\u{51FA}\u{56FE}\u{4F20}\u{9001}\u{95E8}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
                    },
                    hint_client_x = 705.204834,
                    hint_client_y = 726.439941,
                    hint_ratio_x = 0.489726,
                    hint_ratio_y = 0.807155,
                    hint_max_distance = 180.000,
                    prefer_hint_fallback = true
                },
                interact_distance = 260,
                retry_ms = 1500,
                settle_ms = 5000,
                fallback_interact = true,
                direct_nearest_button = true
            }
        },
        restart_landing = {
            x = -150.00,
            y = -200.00,
            z = 56.00,
            radius = 1200,
            z_tolerance = 260
        },
        exit_landing = {
            x = 5642.69,
            y = -530.40,
            z = 503.00,
            radius = 2400,
            z_tolerance = 900
        },
        transition_timeout_ms = 15000,
        notes = {
            "Enabled for entry / route capture based on the other working treasure baselines",
            "Known outside entrance: 5642.69,-530.40,503.00",
            "Known side task text: 藏宝地：隐世金阁 -> 通关1次藏宝地：隐世金阁",
            "F7 map field read as 有1点新天赋点 near the entry, so real outside map name is still missing",
            "Measured inside post-send landing observed in latest logs near -150,-200,56; use it as the local inside signal until a real inside map name is confirmed",
            "Target level requested: 46",
            "Entry follows the same two-step baseline as the other working treasures: MapTrapBtn then SendBtn",
            "Entry UI locator is still placeholder-level and needs real F8/F10 data for both doorway and SendBtn",
            "Outside side-task panel already shows 藏宝地：隐世金阁, so entering must not use panel_query as the enter-detected signal; rely on inside map / route capture instead",
            "Current route action abyss_below_fourth_treasure_entry_5643_-530 should only pull the character to the entrance; once inside entry_trigger the treasure module should take over before normal route actions",
            "Boss trigger uses measured center 10670.88,18175.38,-664 with kite points 11711.53,19285.31,-664 / 9807.94,19361.28,-664 / 10670.88,18175.38,-664",
            "Restart portal trigger uses measured door 11010,19761,-664; restart_landing reuses the measured first inside landing near -150,-200,56",
            "Exit portal trigger now uses the measured F7 EPortal position 10150,19750,-664; earlier player stand point 10152,19705,-664 is treated as the same exit door and only kept as supporting evidence",
            "This F7 exit portal can be used as the fourth treasure boss-death/portal-ready signal because it matches the configured exit door, not the restart door",
            "Exit_landing reuses the original outside treasure entrance near 5642.69,-530.40,503.00",
            "Reject any acquired route whose first point falls near the outside mainline goal 3050,7050,503; that path is a false positive from the current 深渊以下 outside route, not an inside-treasure route",
            "Restart_landing is too close to the later mainline route in 陷落圣城, so it must not drive startup/inside inference for this treasure; use inside_landing/map signals instead",
            "If a captured route reaches terminal cursor 1 outside the boss trigger, release the treasure route instead of suppressing mainline task_pos forever",
            "Missing: reliable outside map name, exact entry / restart / exit button locator logs, inside map name, and restart/exit landing runtime verification"
        }
    },
    {
        -- Enabled for entry / route capture. Only the outside entrance and
        -- entry MapTrapBtn are verified; inside boss / restart / exit data
        -- still needs live F6/F7/F10 collection after the first entry.
        key = "treasure_silver_sand_edge_city_entry_-7886_-4560",
        enabled = true,
        name = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{94F6}\u{6C99}\u{8FB9}\u{57CE}",
        route_store_key = "treasure_silver_sand_edge_city_entry_-7886_-4560_v1",
        target_level = 57,
        inside_detect_task_panel_text = false,
        allow_when_task_unknown = true,
        startup_recovery_wait_for_task_panel = true,
        startup_recovery_activate_by_level_gate = true,
        startup_recovery_allow_task_mismatch_by_level_gate = true,
        startup_recovery_allow_inside_landing_task_mismatch_by_level_gate = true,
        startup_recovery_task_panel_wait_ms = 1800,
        startup_recovery_task_panel_wait_cap_ms = 9000,
        restart_landing_require_verified = true,
        task_patterns = {
            "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{94F6}\u{6C99}\u{8FB9}\u{57CE}",
            "\u{94F6}\u{6C99}\u{8FB9}\u{57CE}",
            "\u{7FA4}\u{661F}\u{4E4B}\u{8F89}"
        },
        task_detail_patterns = {
            "\u{65B0}",
            "\u{5E2E}\u{52A9}\u{665A}\u{661F}\u{51FB}\u{6E83}\u{4F0A}\u{5409}\u{738B}\u{519B}\u{FF0C}\u{627E}\u{5230}\u{72EE}\u{5FC3}\u{738B}"
        },
        map_patterns = {
            "\u{96C4}\u{72EE}\u{4E4B}\u{5FC3}"
        },
        inside_map_patterns = {
            "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{94F6}\u{6C99}\u{8FB9}\u{57CE}",
            "\u{94F6}\u{6C99}\u{8FB9}\u{57CE}"
        },
        inside_landing = {
            x = 0.00,
            y = 0.00,
            z = 1662.00,
            radius = 60,
            z_tolerance = 260,
            allow_zero = true
        },
        restart_landing = {
            x = 0.00,
            y = 0.00,
            z = 1662.00,
            radius = 60,
            z_tolerance = 260,
            allow_zero = true
        },
        extra_restart_landings = {
            {
                x = 341.65,
                y = 265.78,
                z = 1662.00,
                radius = 620,
                z_tolerance = 260
            },
            {
                x = 689.36,
                y = 536.27,
                z = 1662.00,
                radius = 720,
                z_tolerance = 260
            }
        },
        entry_trigger = {
            x = -7886.00,
            y = -4560.00,
            z = 1921.62,
            radius = 520,
            z_tolerance = 520
        },
        entry_steps = {
            {
                key = "treasure_silver_sand_edge_city_map_trap",
                label = "\u{94F6}\u{6C99}\u{8FB9}\u{57CE}\u{5165}\u{53E3}MapTrapBtn",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                },
                hint_client_x = 706.528564,
                hint_client_y = 707.631531,
                hint_ratio_x = 0.490645,
                hint_ratio_y = 0.786257,
                hint_max_distance = 110.000,
                prefer_hint_fallback = true,
                hover_capture_enabled = true,
                hover_capture_client_left = 690.0,
                hover_capture_client_top = 685.0,
                hover_capture_client_right = 745.0,
                hover_capture_client_bottom = 730.0,
                hover_capture_retry_ms = 700,
                settle_ms = 1500,
                retry_ms = 2500
            },
            {
                key = "treasure_silver_sand_edge_city_send",
                label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
                trigger = {
                    x = -7886.00,
                    y = -4560.00,
                    z = 1921.62,
                    radius = 180,
                    z_tolerance = 520
                },
                interact_distance = 60,
                distance_anchor_exact_text = "\u{4F20}\u{9001}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
                distance_min = 8.248740,
                distance_max = 9.248740,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
                },
                hint_client_x = 661.188843,
                hint_client_y = 827.716736,
                hint_ratio_x = 0.459159,
                hint_ratio_y = 0.919685,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                hover_capture_enabled = true,
                hover_capture_client_left = 654.0,
                hover_capture_client_top = 789.0,
                hover_capture_client_right = 790.0,
                hover_capture_client_bottom = 810.0,
                hover_capture_retry_ms = 900,
                settle_ms = 1800,
                retry_ms = 2500
            }
        },
        portals = {
            restart = {
                key = "treasure_silver_sand_edge_city_restart_portal",
                kind = "restart",
                trigger = {
                    x = 27603.00,
                    y = 21473.00,
                    z = 1662.00,
                    radius = 560,
                    z_tolerance = 420
                },
                step = {
                    key = "treasure_silver_sand_edge_city_restart_portal_btn",
                    label = "silver_sand_restart_MapTrapBtn",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                    },
                    hint_client_x = 706.528564,
                    hint_client_y = 707.631531,
                    hint_ratio_x = 0.490645,
                    hint_ratio_y = 0.786257,
                    hint_max_distance = 180.000,
                    prefer_hint_fallback = true,
                    hover_capture_enabled = true,
                    hover_capture_client_left = 690.0,
                    hover_capture_client_top = 685.0,
                    hover_capture_client_right = 745.0,
                    hover_capture_client_bottom = 730.0,
                    hover_capture_retry_ms = 700
                },
                interact_distance = 360,
                retry_ms = 1500,
                settle_ms = 5000,
                fallback_interact = true,
                direct_nearest_button = true
            },
            exit = {
                key = "treasure_silver_sand_edge_city_exit_portal",
                kind = "exit",
                trigger = {
                    x = 28042.00,
                    y = 22383.00,
                    z = 1662.00,
                    radius = 560,
                    z_tolerance = 420
                },
                step = {
                    key = "treasure_silver_sand_edge_city_exit_portal_btn",
                    label = "silver_sand_exit_PortalBtn",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
                    },
                    hint_client_x = 697.528564,
                    hint_client_y = 697.631531,
                    hint_ratio_x = 0.484395,
                    hint_ratio_y = 0.775146,
                    hint_max_distance = 180.000,
                    prefer_hint_fallback = true,
                    hover_capture_enabled = true,
                    hover_capture_client_left = 690.0,
                    hover_capture_client_top = 685.0,
                    hover_capture_client_right = 745.0,
                    hover_capture_client_bottom = 730.0,
                    hover_capture_retry_ms = 700
                },
                interact_distance = 360,
                retry_ms = 1500,
                settle_ms = 5000,
                fallback_interact = true,
                direct_nearest_button = true
            }
        },
        exit_landing = {
            x = 0.00,
            y = 0.00,
            z = 1662.00,
            radius = 60,
            z_tolerance = 260,
            allow_zero = true
        },
        entry_ui_retry_timeout_ms = 6500,
        enter_detect_task_panel_query = false,
        panel_query = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{94F6}\u{6C99}\u{8FB9}\u{57CE}",
        panel_query_fallbacks = {
            "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{94F6}\u{6C99}\u{8FB9}\u{57CE}",
            "\u{94F6}\u{6C99}\u{8FB9}\u{57CE}",
            "\u{85CF}\u{5B9D}\u{5730}"
        },
        panel_button_step = {
            key = "treasure_silver_sand_edge_city_task_button",
            label = "\u{652F}\u{7EBF} \u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{94F6}\u{6C99}\u{8FB9}\u{57CE}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{652F}\u{7EBF} \u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{94F6}\u{6C99}\u{8FB9}\u{57CE}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn",
            distance_anchor_required = true,
            distance_min = 28.000000,
            distance_max = 42.000000,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn"
            },
            hint_client_x = 85.907120,
            hint_client_y = 292.023743,
            hint_ratio_x = 0.059658,
            hint_ratio_y = 0.324111,
            hint_max_distance = 80.000
        },
        panel_button_step_required = true,
        path_retry_count = 5,
        path_retry_interval_ms = 1200,
        min_path_points = 3,
        route_refresh_ms = 900,
        route_arrive_tolerance = 150,
        route_reanchor_forward_delta = 8,
        nearby_monster_hard_hold_distance = 200,
        nearby_monster_soft_hold_distance = 350,
        nearby_monster_soft_hold_timeout_ms = 4000,
        loot_press_interval_ms = 350,
        loot_stuck_max_attempts = 8,
        loot_ignore_ms = 12000,
        route_simplify = {
            min_spacing = 240,
            z_keep_delta = 90,
            turn_cos_threshold = 0.9910
        },
        boss = {
            enabled = true,
            loot_enabled = true,
            loot_anchor_distance = 900,
            loot_settle_ms = 2800,
            loot_empty_confirm_ms = 2200,
            loot_empty_confirm_without_seen = true,
            trigger = {
                use_route_destination = true,
                radius = 1500,
                z_tolerance = 520
            },
            kite_points = {
                { x = 26898.02, y = 22608.14, z = 1662.00 },
                { x = 25794.32, y = 21898.75, z = 1662.00 },
                { x = 26792.32, y = 21265.38, z = 1662.00 }
            },
            kite_radius = 2200,
            pre_engage_anchor_distance = 260,
            zero_monster_grace_ms = 900,
            clear_settle_ms = 3200
        },
        transition_timeout_ms = 15000,
        notes = {
            "Known current outside task: 藏宝地：银沙边城 -> 新 on 雄狮之心",
            "Known mainline around trigger: 群星之辉 -> 帮助晚星击溃伊吉王军，找到狮心王",
            "Outside entrance measured at -7886,-4560,1921.62",
            "Inside landing and restart landing verified at 0,0,1662; allow_zero is intentionally scoped to this dungeon landing",
            "The 0,0,1662 landing radius is intentionally narrow so startup can recover from exact treasure spawn without matching nearby mainline coordinates",
            "Restart landing also observed after the latest restart click at 341.65,265.78 and 689.36,536.27; accept both as restart landing so the portal phase does not walk back to the old restart door",
            "Restart portal trigger measured at 27603,21473,1662; exit portal trigger measured at 28042,22383,1662",
            "Exit landing reuses the same first-entry landing at 0,0,1662; runtime only treats it as exit while in exit/return flow",
            "Boss room center is the acquired treasure route destination; kite points verified at 26898.02,22608.14 / 25794.32,21898.75 / 26792.32,21265.38",
            "Entry button verified by F10 GetCurrentSelected as FightInteractiveView_C.WidgetTree.MapTrapBtn near client 706.53,707.63",
            "The nearby 末日重斧 text is ground-item noise and is intentionally not used as a locator anchor",
            "Missing: live verification of boss portal enum and loot completion after this boss config"
        }
    }
}

apply_treasure_dungeon_baseline_defaults(M.TREASURE_DUNGEON_CONFIGS)

M.TASK_OBJECTIVE_BUTTON_STEPS = {
    {
        key = "gather_btn",
        label = "72/115鎸夐挳",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn"
        },
        hint_client_x = 697.204834,
        hint_client_y = 724.439941,
        hint_ratio_x = 0.484170,
        hint_ratio_y = 0.804933,
        hint_max_distance = 80.000,
        settle_ms = 1200
    },
    {
        key = "function_btn",
        label = "\u{4EA4}\u{4E92}\u{4E2D}\u{6309}\u{94AE}",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.FunctionBtn",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.FunctionBtn"
        },
        hint_client_x = 697.204834,
        hint_client_y = 724.439941,
        hint_ratio_x = 0.484170,
        hint_ratio_y = 0.804933,
        hint_max_distance = 80.000,
        settle_ms = 1200
    },
    {
        key = "transport_btn",
        label = "TransportBtn",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.TransportBtn",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.TransportBtn"
        },
        hint_client_x = 696.204834,
        hint_client_y = 724.439941,
        hint_ratio_x = 0.483476,
        hint_ratio_y = 0.804933,
        hint_max_distance = 80.000,
        settle_ms = 1200
    },
    {
        key = "jump_btn",
        label = "5鎸夐挳",
        distance_anchor_exact_text = "跳过",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.DialogueTalk_C.WidgetTree.JumpBtn",
        distance_min = 29.408105,
        distance_max = 31.227163,
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.DialogueTalk_C.WidgetTree.JumpBtn"
        },
        hint_client_x = 1314.729858,
        hint_client_y = 66.760315,
        hint_ratio_x = 0.913007,
        hint_ratio_y = 0.074178,
        hint_max_distance = 80.000,
        hover_capture_retry_ms = 900,
        hover_capture_restore_cursor_on_failure = true,
        hover_capture_client_left = 1320.0,
        hover_capture_client_top = 36.0,
        hover_capture_client_right = 1363.0,
        hover_capture_client_bottom = 49.0,
        hover_capture_sample_attempts = 20,
        hover_capture_history_limit = 8,
        hover_capture_zone_cols = 3,
        hover_capture_zone_rows = 2,
        hover_capture_min_point_gap = 8.0,
        hover_capture_move_min_floor_ms = 35,
        hover_capture_move_min_ceil_ms = 90,
        hover_capture_move_span_min_ms = 35,
        hover_capture_move_span_max_ms = 95,
        hover_capture_hover_min_ms = 55,
        hover_capture_hover_max_ms = 145,
        hover_return_safe_x_ratio_min = 0.34,
        hover_return_safe_x_ratio_max = 0.68,
        hover_return_safe_y_ratio_min = 0.30,
        hover_return_safe_y_ratio_max = 0.62,
        hover_return_sample_attempts = 18,
        hover_return_history_limit = 8,
        hover_return_zone_cols = 4,
        hover_return_zone_rows = 3,
        hover_return_min_point_gap = 52.0,
        hover_return_move_min_floor_ms = 45,
        hover_return_move_min_ceil_ms = 110,
        hover_return_move_span_min_ms = 45,
        hover_return_move_span_max_ms = 120,
        hover_return_hover_min_ms = 20,
        hover_return_hover_max_ms = 80,
        settle_ms = 500
    }
}

M.MAP_TASK_CONFIGS = {
    ["杩滃彜閫氶亾"] = {
        transitions = {
            {
                key = "wanderer_boots_portal",
                label = "\u{8FDC}\u{53E4}\u{901A}\u{9053}_\u{6E38}\u{8361}\u{8005}\u{957F}\u{9774}\u{4F20}\u{9001}",
                trigger = {
                    x = 2568.74,
                    y = 19312.33,
                    radius = 520
                },
                settle_ms = 1200,
                retry_ms = 5000,
                step = {
                    label = "\u{6E38}\u{8361}\u{8005}\u{957F}\u{9774}\u{6309}\u{94AE}",
                    distance_anchor_exact_text = "\u{6E38}\u{8361}\u{8005}\u{957F}\u{9774}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
                    distance_min = 213.430774,
                    distance_max = 218.430774,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
                    },
                    hint_client_x = 705.204834,
                    hint_client_y = 726.439941,
                    hint_ratio_x = 0.489726,
                    hint_ratio_y = 0.807155,
                    hint_max_distance = 80.000
                }
            }
        }
    },
    ["涓婂彜鎴樺満"] = {
        objective = {
            key = "boss_room",
            mode = "boss_kite",
            trigger_distance = 360,
            skip_direct_interact = true,
            allow_any_monster = true,
            force_kite = true
        },
        revive_reentry = {
            key = "boss_return_portal",
            label = "\u{4E0A}\u{53E4}\u{6218}\u{573A}\u{590D}\u{6D3B}\u{56DE}boss\u{95E8}",
            anchor = {
                x = 10318.68,
                y = -7239.43,
                z = 566.00,
                radius = 1200
            },
            portal_max_distance = 1800,
            interact_distance = 240,
            retry_ms = 1200,
            settle_ms = 1200
        }
    }
}

local function make_empire_ashes_head_wolf_dodge_boss_task_config()
    return make_boss_kite_task_config(
        "empire_ashes_head_wolf_dodge_boss_room",
        {
            trigger_distance = 520,
            kite_radius = 2600,
            kite_switch_ms = 2800,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            defer_followup_until_clear = true,
            ignore_terminal_text_change_when_objective_same = true,
            kite_points = {
                { x = 13536.51, y = 15323.40, z = 906.00 },
                { x = 13201.13, y = 16535.58, z = 906.00 },
                { x = 12125.71, y = 16047.29, z = 906.00 }
            }
        },
        {
            task_patterns = {
                "\u{5E1D}\u{56FD}\u{4F59}\u{7130}",
                "\u{5BFB}\u{627E}\u{7FA4}\u{72FC}\u{5E2E}\u{9996}\u{9886}"
            },
            task_detail_patterns = {
                "\u{6253}\u{8D25}\u{201C}\u{5934}\u{72FC}\u{201D}\u{9053}\u{5947}",
                "\u{5BFB}\u{627E}\u{7FA4}\u{72FC}\u{5E2E}\u{9996}\u{9886}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_free_fire_elsa_approval_boss_task_config()
    return make_boss_kite_task_config(
        "free_fire_elsa_approval_boss_room",
        {
            trigger_distance = 520,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 1200,
            nearby_text_task_change_confirm_count = 2,
            nearby_text_task_change_exit_patterns = {
                "\u{4E0E}\u{4F0A}\u{5C14}\u{838E}\u{5BF9}\u{8BDD}"
            },
            ignore_terminal_text_change_when_objective_same = true
        },
        {
            task_patterns = {
                "\u{81EA}\u{7531}\u{7684}\u{7130}\u{706B}",
                "\u{53D6}\u{5F97}\u{4F0A}\u{5C14}\u{838E}\u{7684}\u{8BA4}\u{53EF}",
                "\u{94F6}\u{7130}\u{9996}\u{9886}\u{00B7}\u{4F0A}\u{5C14}\u{838E}"
            },
            task_detail_patterns = {
                "\u{53D6}\u{5F97}\u{4F0A}\u{5C14}\u{838E}\u{7684}\u{8BA4}\u{53EF}",
                "\u{94F6}\u{7130}\u{9996}\u{9886}\u{00B7}\u{4F0A}\u{5C14}\u{838E}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_late_star_royal_encirclement_boss_task_config(opts)
    opts = type(opts) == "table" and opts or {}
    local allow_no_target = opts.allow_no_target == true
    local detail_pattern = tostring(opts.detail_pattern or "")
    local detail_patterns = {}
    if detail_pattern ~= "" then
        detail_patterns[#detail_patterns + 1] = detail_pattern
    end
    return make_boss_kite_task_config(
        "late_star_royal_encirclement_boss_kite",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = allow_no_target,
            immediate_no_task_target_kite = allow_no_target,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true
        },
        {
            task_patterns = {
                "\u{665A}\u{661F}\u{5F85}\u{660E}"
            },
            task_detail_patterns = detail_patterns,
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_breakthrough_royal_message_boss_task_config(opts)
    opts = type(opts) == "table" and opts or {}
    local allow_no_target = opts.allow_no_target == true
    local detail_pattern = tostring(opts.detail_pattern or "")
    local detail_patterns = {}
    if detail_pattern ~= "" then
        detail_patterns[#detail_patterns + 1] = detail_pattern
    end
    return make_boss_kite_task_config(
        tostring(opts.key or "breakthrough_royal_message_boss_kite"),
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = allow_no_target,
            immediate_no_task_target_kite = allow_no_target,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true
        },
        {
            task_patterns = {
                "突破重围"
            },
            task_detail_patterns = detail_patterns,
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    )
end

local function make_long_plan_elsa_intel_dialogue_task_config()
    return {
        objective = {
            key = "long_plan_elsa_intel_dialogue",
            followup_route_action_key = "long_plan_elsa_intel_dialogue_-2430_5050",
            trigger_distance = 420
        },
        task_patterns = {
            "\u{4ECE}\u{957F}\u{8BA1}\u{8BAE}",
            "\u{8FD4}\u{56DE}\u{9ECE}\u{660E}\u{5723}\u{6240}"
        },
        task_detail_patterns = {
            "\u{548C}\u{4F0A}\u{5C14}\u{838E}\u{4EA4}\u{6D41}\u{60C5}\u{62A5}"
        },
        constraint_mode = "all"
    }
end

local function make_another_magic_academy_forbidden_guard_revive_reentry_config()
    return make_revive_reentry_config({
        key = "another_magic_academy_forbidden_guard_boss_room_reentry_18360_8571",
        label = "\u{53E6}\u{4E00}\u{4E2A}\u{9B54}\u{6CD5}\u{5B66}\u{9662} Boss\u{91CD}\u{8FDB}\u{623F}",
        anchor = {
            x = 18360.00,
            y = 8571.00,
            z = 308.00,
            radius = 620,
            z_tolerance = 320
        },
        interact_distance = 360,
        portal_scan_distance = 900,
        retry_ms = 900,
        settle_ms = 1400,
        timeout_ms = 22000,
        post_transition_boss_engage_ms = 16000,
        fallback_interact = true
    })
end

local function make_another_magic_academy_forbidden_guard_boss_task_config()
    return make_boss_kite_task_config(
        "another_magic_academy_forbidden_guard_boss_room",
        {
            trigger_distance = 520,
            immediate_kite_on_reached = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            revive_reentry = make_another_magic_academy_forbidden_guard_revive_reentry_config()
        },
        {
            task_patterns = {
                "\u{53E6}\u{4E00}\u{4E2A}\u{9B54}\u{6CD5}\u{5B66}\u{9662}"
            },
            task_detail_patterns = {
                "\u{51B2}\u{7834}\u{9632}\u{7EBF}\u{FF0C}\u{62B5}\u{8FBE}\u{7981}\u{533A}\u{5165}\u{53E3}",
                "\u{51FB}\u{8D25}\u{7981}\u{533A}\u{5B88}\u{536B}",
                "\u{5B88}\u{536B}\u{519B}\u{9886}\u{8896}\u{00B7}\u{963F}\u{5C14}\u{514B}\u{65AF}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_fire_treader_romel_boss_task_config()
    return make_boss_kite_task_config(
        "fire_treader_romel_boss_room",
        {
            trigger_distance = 520,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 180,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true
        },
        {
            task_patterns = {
                "\u{8E48}\u{706B}\u{4E4B}\u{4EBA}",
                "\u{51FB}\u{8D25}\u{88AB}\u{9B54}\u{6CD5}\u{5B66}\u{9662}\u{6539}\u{9020}\u{7684}\u{7F57}\u{6885}\u{5C14}",
                "\u{5B9E}\u{9A8C}\u{4F53}\u{00B7}\u{7F57}\u{6885}\u{5C14}"
            },
            task_detail_patterns = {
                "\u{51FB}\u{8D25}\u{88AB}\u{9B54}\u{6CD5}\u{5B66}\u{9662}\u{6539}\u{9020}\u{7684}\u{7F57}\u{6885}\u{5C14}",
                "\u{5B9E}\u{9A8C}\u{4F53}\u{00B7}\u{7F57}\u{6885}\u{5C14}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_overcast_city_awakened_ryan_task_config()
    return make_boss_kite_task_config(
        "overcast_city_awakened_ryan_kite",
        {
            trigger_distance = 180,
            immediate_kite_on_reached = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 180,
            boss_clear_settle_ms = 3500,
            defer_followup_until_clear = true,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true
        },
        {
            task_patterns = {
                "\u{9634}\u{4E91}\u{538B}\u{57CE}"
            },
            task_detail_patterns = {
                "\u{524D}\u{5F80}\u{706B}\u{79CD}\u{88C5}\u{7F6E}",
                "\u{5B88}\u{62A4}\u{706B}\u{79CD}\u{88C5}\u{7F6E}",
                "\u{706B}\u{79CD}\u{88C5}\u{7F6E}",
                "\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}",
                "\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}",
                "\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_overcast_city_ask_liv_key_sequence_task_config()
    return {
        entry_action = {
            key = "overcast_city_ask_liv_press_a_after_task_call",
            mode = "key_sequence",
            hotkey = "A",
            repeat_count = 5,
            interval_ms = 140,
            initial_delay_ms = 250,
            timeout_ms = 5000,
            label = "阴云压城_询问丽芙情况_A键确认"
        },
        task_patterns = {
            "\u{9634}\u{4E91}\u{538B}\u{57CE}"
        },
        task_detail_patterns = {
            "\u{8BE2}\u{95EE}\u{4E3D}\u{8299}\u{60C5}\u{51B5}"
        },
        constraint_mode = "all"
    }
end

local function make_journey_begin_awakened_leader_reentry_config()
    return make_revive_reentry_config({
        key = "journey_begin_awakened_leader_reentry_-1542_10164",
        label = "旅途之始_觉醒者头目Boss重进房",
        anchor = {
            x = -1542.00,
            y = 10164.00,
            z = 599.19,
            radius = 560
        },
        interact_distance = 280,
        retry_ms = 900,
        settle_ms = 1000,
        timeout_ms = 22000,
        post_transition_boss_engage_ms = 16000,
        fallback_interact = true,
        skip_post_revive_task_path_reacquire = true
    })
end

local function make_journey_begin_awakened_leader_task_config()
    return make_boss_kite_task_config(
        "journey_begin_awakened_leader_kite",
        {
            trigger_distance = 180,
            immediate_kite_on_reached = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 1200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 180,
            boss_clear_settle_ms = 3500,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            revive_reentry = make_journey_begin_awakened_leader_reentry_config()
        },
        {
            task_patterns = {
                "\u{65C5}\u{9014}\u{4E4B}\u{59CB}",
                "\u{7A81}\u{7834}\u{89C9}\u{9192}\u{8005}\u{91CD}\u{56F4}",
                "\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}"
            },
            task_detail_patterns = {
                "\u{7A81}\u{7834}\u{89C9}\u{9192}\u{8005}\u{91CD}\u{56F4}",
                "\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}",
                "\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_ancient_battlefield_trace_ryan_task_config()
    return make_boss_kite_task_config(
        "ancient_battlefield_trace_ryan_kite",
        {
            trigger_distance = 1550,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            require_task_path_for_kite = true,
            post_revive_force_task_path_reacquire = true,
            post_revive_task_path_reacquire_wait_ms = 900,
            post_revive_task_pos_reject_extra_ms = 5500,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 180,
            kite_points = {
                { x = 2105.00, y = -1673.00, z = 566.00 },
                { x = 215.00, y = -581.81, z = 566.00 },
                { x = 215.00, y = -2764.79, z = 566.00 }
            },
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3500,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true
        },
        {
            task_patterns = {
                "\u{4E0A}\u{53E4}\u{6218}\u{573A}"
            },
            task_detail_patterns = {
                "\u{5E26}\u{4E0A}\u{79D1}\u{91CC}\u{FF0C}\u{7EE7}\u{7EED}\u{8FFD}\u{8E2A}\u{83B1}\u{5B89}",
                "\u{7B49}\u{5F85}\u{79D1}\u{91CC}\u{5C06}\u{6728}\u{6865}\u{4FEE}\u{597D}",
                "\u{63A9}\u{62A4}\u{79D1}\u{91CC}\u{4FEE}\u{6865}"
            },
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_holy_fire_roadblock_awakened_task_config(fight_detail_name)
    return make_boss_kite_task_config(
        "holy_fire_roadblock_awakened_kite",
        {
            trigger_distance = 760,
            immediate_kite_on_reached = true,
            kite_radius = 1200,
            kite_switch_ms = 1800,
            seamless_kite = true,
            kite_arrive_distance = 320,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3500,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            post_combat_loot = false,
            kite_points = {
                { x = 20510.00, y = 15840.00, z = 1714.00 },
                { x = 19902.54, y = 15968.00, z = 1714.00 },
                { x = 20431.59, y = 15780.33, z = 1714.00 },
                { x = 20013.27, y = 15931.84, z = 1714.00 }
            }
        },
        {
            task_patterns = {
                fight_detail_name
            },
            task_detail_patterns = {
                fight_detail_name
            }
        }
    )
end

local function make_fanmu_blocking_deputy_task_config()
    return make_boss_kite_task_config(
        "fanmu_blocking_deputy_kite",
        {
            trigger_distance = 220,
            immediate_kite_on_reached = true,
            seamless_kite = true,
            kite_switch_ms = 1600,
            kite_arrive_distance = 280,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            kite_points = {
                { x = -580.00, y = -620.00, z = 16.00 },
                { x = -842.00, y = -850.00, z = 16.00 },
                { x = -292.00, y = -922.00, z = 16.00 }
            }
        },
        {
            task_patterns = {
                "\u{53CD}\u{76EE}"
            },
            task_detail_patterns = {
                "\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{526F}\u{5B98}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_escape_inner_city_exit_hass_kite_task_config(detail_patterns, opts)
    opts = opts or {}
    local objective = {
        trigger_distance = 900,
        immediate_kite_on_reached = true,
        require_task_path_for_kite = opts.require_task_path_for_kite ~= false,
        kite_anchor_source = "task_destination",
        kite_radius = 1260,
        kite_point_count = 3,
        seamless_kite = true,
        kite_switch_ms = 2400,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 180,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3500,
        generic_followup_refresh_ms = 3500,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        ignore_terminal_text_change_when_objective_same = true
    }
    if opts.no_task_target == true then
        objective.allow_no_task_target_force_kite = true
        objective.immediate_no_task_target_kite = true
        objective.no_task_target_kite_wait_ms = tonumber(opts.no_task_target_kite_wait_ms) or 900
        objective.require_task_path_for_kite = false
    end
    return make_boss_kite_task_config(
        "escape_inner_city_exit_hass_endpoint_kite",
        objective,
        {
            task_patterns = {
                "\u{9003}\u{79BB}\u{5185}\u{57CE}\u{533A}"
            },
            task_detail_patterns = detail_patterns,
            constraint_mode = "all"
        }
    )
end

local function make_wall_of_sighs_city_guard_task_config()
    return make_boss_kite_task_config(
        "wall_of_sighs_city_guard_room_kite",
        {
            trigger_distance = 360,
            immediate_kite_on_reached = true,
            seamless_kite = true,
            kite_switch_ms = 1800,
            kite_arrive_distance = 360,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            kite_points = {
                { x = 41722.66, y = 16692.50, z = 5363.00 },
                { x = 41752.31, y = 18232.59, z = 5363.00 },
                { x = 40715.92, y = 17264.01, z = 5369.00 }
            }
        },
        {
            task_patterns = {
                "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
            },
            task_detail_patterns = {
                "\u{51FB}\u{8D25}\u{9A7B}\u{5B88}\u{57CE}\u{5899}\u{7684}\u{5B66}\u{57CE}\u{5B88}\u{536B}"
            },
            constraint_mode = "all"
        }
    )
end

local function wall_of_sighs_final_mechanism_kite_points()
    return {
        { x = 17850.98, y = -1722.21, z = 404.91 },
        { x = 18286.77, y = -1240.62, z = 403.00 },
        { x = 18873.88, y = -1170.40, z = 403.00 },
        { x = 19287.23, y = -1559.87, z = 403.00 },
        { x = 19406.10, y = -2068.39, z = 403.00 },
        { x = 19289.77, y = -2547.40, z = 403.00 },
        { x = 18918.86, y = -2933.72, z = 403.00 },
        { x = 18475.91, y = -2973.80, z = 403.00 },
        { x = 18051.16, y = -2828.32, z = 403.00 },
        { x = 17673.04, y = -2483.23, z = 405.20 },
        { x = 17341.30, y = -1990.53, z = 377.41 }
    }
end

local function make_wall_of_sighs_final_mechanism_kite_task_config(detail_patterns, opts)
    opts = opts or {}
    local objective = {
        trigger_distance = 2600,
        immediate_kite_on_reached = true,
        allow_no_task_target_force_kite = true,
        seamless_kite = true,
        kite_switch_ms = 1800,
        kite_arrive_distance = 360,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        generic_followup_refresh_ms = 3500,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        ignore_terminal_text_change_when_objective_same = true,
        kite_points = wall_of_sighs_final_mechanism_kite_points()
    }
    if opts.immediate_no_task_target == true then
        objective.immediate_no_task_target_kite = true
        objective.no_task_target_kite_wait_ms = tonumber(opts.no_task_target_kite_wait_ms) or 900
    end
    return make_boss_kite_task_config(
        "wall_of_sighs_final_mechanism_guard_loop",
        objective,
        {
            task_patterns = {
                "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
            },
            task_detail_patterns = detail_patterns,
            constraint_mode = "all"
        }
    )
end

local function make_wall_of_sighs_will_wall_boss_task_config()
    return make_wall_of_sighs_final_mechanism_kite_task_config({
        "\u{7EE7}\u{7EED}\u{524D}\u{8FDB}\u{FF0C}\u{7A7F}\u{8D8A}\u{610F}\u{5FD7}\u{9AD8}\u{5899}"
    })
end

local function make_wall_of_sighs_final_mechanism_task_config()
    return make_wall_of_sighs_final_mechanism_kite_task_config({
        "\u{6D88}\u{706D}\u{5B88}\u{536B}\u{FF0C}\u{5173}\u{95ED}\u{6700}\u{7EC8}\u{673A}\u{5173}"
    }, {
        immediate_no_task_target = true
    })
end

local function make_tianqian_cross_wall_task_config()
    return make_boss_kite_task_config(
        "tianqian_cross_wall_room_kite",
        {
            trigger_distance = 1100,
            immediate_kite_on_reached = true,
            kite_radius = 2200,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            kite_points = {
                { x = 7815.17, y = 13654.82, z = 86.00 },
                { x = 7996.41, y = 12194.96, z = 86.00 },
                { x = 9564.06, y = 12196.62, z = 86.00 },
                { x = 9654.52, y = 13564.38, z = 86.00 }
            }
        },
        {
            task_patterns = {
                "\u{5929}\u{5811}\u{6B67}\u{8DEF}"
            },
            task_detail_patterns = {
                "\u{7EE7}\u{7EED}\u{524D}\u{8FDB}\u{FF0C}\u{7A7F}\u{8D8A}\u{5DE8}\u{5899}"
            },
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_tianqian_guard_cannon_awakened_task_config()
    return make_boss_kite_task_config(
        "tianqian_guard_cannon_awakened_room_kite",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            kite_radius = 2600,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            revive_reentry = make_revive_reentry_config({
                key = "tianqian_guard_cannon_awakened_room_reentry_16219_18567",
                label = "天堑歧路_巨炮守护者_Boss重进房",
                anchor = {
                    x = 16219.00,
                    y = 18567.00,
                    z = 108.16,
                    radius = 620,
                    z_tolerance = 320
                },
                interact_distance = 320,
                portal_scan_distance = 900,
                retry_ms = 900,
                settle_ms = 1400,
                timeout_ms = 24000,
                post_transition_boss_engage_ms = 16000,
                fallback_interact = true
            }),
            kite_points = {
                { x = -1446.32, y = 3412.12, z = 2446.00 },
                { x = -894.30,  y = 4426.68, z = 2446.00 },
                { x = 59.81,    y = 3793.43, z = 2446.00 },
                { x = -813.77,  y = 2941.04, z = 2446.00 }
            }
        },
        {
            task_patterns = {
                "\u{5929}\u{5811}\u{6B67}\u{8DEF}"
            },
            task_detail_patterns = {
                "\u{51FB}\u{8D25}\u{5B88}\u{62A4}\u{5DE8}\u{70AE}\u{7684}\u{89C9}\u{9192}\u{8005}"
            },
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_dragonbone_griffin_boss_task_config()
    return make_boss_kite_task_config(
        "dragonbone_griffin_boss",
        {
            trigger_distance = 520,
            immediate_kite_on_reached = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 2500,
            generic_followup_refresh_ms = 3000,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            revive_reentry = make_revive_reentry_config({
                key = "dragonbone_griffin_reentry_1891_6171",
                label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}\u{72EE}\u{9E6B}Boss\u{91CD}\u{8FDB}\u{623F}",
                anchor = {
                    x = 1891.00,
                    y = 6171.00,
                    z = 1192.00,
                    radius = 560
                },
                interact_distance = 300,
                portal_scan_distance = 900,
                retry_ms = 900,
                settle_ms = 1400,
                timeout_ms = 22000,
                post_transition_boss_engage_ms = 16000,
                fallback_interact = true
            })
        },
        {
            task_patterns = {
                "\u{9F99}\u{9668}\u{4E4B}\u{91CE}"
            },
            task_detail_patterns = {
                "\u{6DF1}\u{5165}\u{9F99}\u{9AA8}\u{5C71}\u{810A}\u{8179}\u{5730}",
                "\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
                "\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
                "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"
            },
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            },
            constraint_mode = "all",
            retry_call_task = {
                enabled = true,
                interval_ms = 1200,
                require_no_progress_ms = 1100,
                require_point_stagnant_ms = 1100,
                deviation_distance = 220,
                off_segment_distance = 160,
                route_endpoint_refresh_ms = 700,
                move_grace_ms = 900
            }
        }
    )
end

local function make_dragonbone_griffin_boss_recovery_task_config()
    return make_boss_kite_task_config(
        "dragonbone_griffin_boss",
        {
            trigger_distance = 520,
            immediate_kite_on_reached = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 2500,
            generic_followup_refresh_ms = 3000,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            revive_reentry = make_revive_reentry_config({
                key = "dragonbone_griffin_reentry_1891_6171_recovery",
                label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}\u{72EE}\u{9E6B}Boss\u{65AD}\u{70B9}\u{91CD}\u{8FDB}\u{623F}",
                anchor = {
                    x = 1891.00,
                    y = 6171.00,
                    z = 1192.00,
                    radius = 560
                },
                interact_distance = 300,
                portal_scan_distance = 900,
                retry_ms = 900,
                settle_ms = 1400,
                timeout_ms = 22000,
                post_transition_boss_engage_ms = 16000,
                fallback_interact = true
            })
        },
        {
            task_patterns = {
                "\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
                "\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
                "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"
            },
            task_detail_patterns = {
                "\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
                "\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
                "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"
            },
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            },
            constraint_mode = "any",
            retry_call_task = {
                enabled = true,
                interval_ms = 1200,
                require_no_progress_ms = 1100,
                require_point_stagnant_ms = 1100,
                deviation_distance = 220,
                off_segment_distance = 160,
                route_endpoint_refresh_ms = 700,
                move_grace_ms = 900
            }
        }
    )
end

local function make_kingdom_end_deep_boss_task_config()
    return make_boss_kite_task_config(
        "kingdom_end_deep_boss_kite",
        {
            trigger_distance = 700,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            revive_reentry = make_revive_reentry_config({
                key = "kingdom_end_ash_mech_room_reentry_5657_-1199",
                label = "王国终途_灰烬机甲_Boss重进房",
                anchor = {
                    x = 5657.21,
                    y = -1199.08,
                    z = -3340.30,
                    radius = 620,
                    z_tolerance = 420
                },
                interact_distance = 320,
                portal_scan_distance = 900,
                retry_ms = 900,
                settle_ms = 1400,
                timeout_ms = 24000,
                post_transition_boss_engage_ms = 16000,
                fallback_interact = true
            })
        },
        {
            task_patterns = {
                "\u{738B}\u{56FD}\u{7EC8}\u{9014}"
            },
            task_detail_patterns = {
                "\u{51FB}\u{8D25}\u{7070}\u{70EC}\u{673A}\u{7532}"
            },
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}",
                "\u{85CF}\u{5B9D}\u{5730}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_wasteland_path_longhorn_task_config()
    return make_boss_kite_task_config(
        "wasteland_path_longhorn_beast_room_25430_12440",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            revive_reentry = make_revive_reentry_config({
                key = "wasteland_path_longhorn_beast_room_reentry_24236_11164",
                label = "灾厄将至_长角异兽_Boss重进房",
                anchor = {
                    x = 24236.00,
                    y = 11164.00,
                    z = 5445.00,
                    radius = 620,
                    z_tolerance = 420
                },
                interact_distance = 320,
                portal_scan_distance = 900,
                retry_ms = 900,
                settle_ms = 1400,
                timeout_ms = 24000,
                post_transition_boss_engage_ms = 16000,
                fallback_interact = true
            })
        },
        {
            task_patterns = {
                "灾厄将至"
            },
            task_detail_patterns = {
                "继续追击莱安",
                "击败拦路的长角异兽"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话",
                "藏宝地"
            },
            constraint_mode = "all"
        }
    )
end

local function make_windbreak_barrier_royal_hunt_boss_task_config(opts)
    opts = type(opts) == "table" and opts or {}
    local allow_no_target = opts.allow_no_target == true
    local detail_pattern = tostring(opts.detail_pattern or "")
    local detail_patterns = {}
    if detail_pattern ~= "" then
        detail_patterns[#detail_patterns + 1] = detail_pattern
    end
    return make_boss_kite_task_config(
        "windbreak_barrier_royal_hunt_kite",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = allow_no_target,
            immediate_no_task_target_kite = allow_no_target,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true
        },
        {
            task_patterns = {
                "遮风壁垒"
            },
            task_detail_patterns = detail_patterns,
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    )
end

local function make_forgotten_temple_special_experiment_keel_task_config()
    return make_boss_kite_task_config(
        "forgotten_temple_special_experiment_keel_kite",
        {
            trigger_distance = 1400,
            immediate_kite_on_reached = true,
            kite_radius = 2400,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 1500,
            nearby_text_task_change_confirm_count = 2,
            kite_points = {
                { x = 9059.86,  y = -248.59, z = 88.00 },
                { x = 10935.20, y = -409.86, z = 88.00 },
                { x = 10051.53, y = 1707.82, z = 88.00 }
            }
        },
        {
            task_patterns = {
                "\u{9057}\u{5FD8}\u{79D8}\u{6BBF}"
            },
            task_detail_patterns = {
                "\u{51FB}\u{8D25}\u{7279}\u{6B8A}\u{5B9E}\u{9A8C}\u{4F53}\u{57FA}\u{5C14}",
                "\u{7279}\u{6B8A}\u{5B9E}\u{9A8C}\u{4F53}\u{57FA}\u{5C14}"
            },
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_abyss_below_awakened_temple_deep_route_task_config()
    return {
        objective = {
            key = "abyss_below_awakened_temple_deep_route",
            followup_route_action_key = "abyss_below_awakened_temple_deep_route_1270_-5810",
            trigger_distance = 420
        },
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{8FDB}\u{5165}\u{89C9}\u{9192}\u{79D8}\u{6BBF}\u{6DF1}\u{5904}"
        },
        constraint_mode = "all"
    }
end

local function make_forgotten_temple_rescue_civilian_route_action(key_suffix, x, y, z)
    return make_route_point_action({
        key = "forgotten_temple_rescue_civilian_" .. tostring(key_suffix),
        label = "forgotten_temple_rescue_civilian_" .. tostring(key_suffix),
        mode = "objective_button_flow_point",
        task_patterns = {
            "\u{9057}\u{5FD8}\u{79D8}\u{6BBF}"
        },
        task_detail_patterns = {
            "\u{62EF}\u{6551}",
            "\u{89E3}\u{6551}"
        },
        constraint_mode = "all",
        trigger = {
            x = x,
            y = y,
            z = z,
            radius = 1400,
            z_tolerance = 360
        },
        require_destination_match = true,
        destination_match_radius = 1600,
        interact_radius = 120,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 1800,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "forgotten_temple_rescue_civilian_gather_btn_" .. tostring(key_suffix),
            label = "forgotten_temple_rescue_civilian_gather_" .. tostring(key_suffix),
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.FunctionBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.FunctionBtn"
            },
            hint_client_x = 706.204834,
            hint_client_y = 726.439941,
            hint_ratio_x = 0.490420,
            hint_ratio_y = 0.807155,
            hint_max_distance = 100.000,
            settle_ms = 1800,
            task_pos_reject_extra_ms = 3500
        }
    })
end

local function make_mountain_heart_dwarf_king_reentry_config()
    return make_revive_reentry_config({
        key = "mountain_heart_dwarf_king_endpoint_reentry_4529_13974",
        label = "群山之心_矮人王Boss重进房",
        anchor = {
            x = 4526.00,
            y = 13974.00,
            z = 57.44,
            radius = 560
        },
        interact_distance = 280,
        retry_ms = 1200,
        settle_ms = 1400,
        timeout_ms = 20000,
        post_transition_boss_engage_ms = 16000,
        fallback_interact = true,
        skip_post_revive_task_path_reacquire = true
    })
end

local function make_plateau_dragonbone_beast_reentry_config()
    return make_revive_reentry_config({
        key = "plateau_dragonbone_beast_reentry_8069_23367",
        label = "龙陨之野_地行异兽Boss重进房",
        anchor = {
            x = 8069.00,
            y = 23367.00,
            z = 5214.00,
            radius = 560
        },
        interact_distance = 280,
        retry_ms = 900,
        settle_ms = 1000,
        timeout_ms = 22000,
        post_transition_boss_engage_ms = 18000,
        fallback_interact = true,
        skip_post_revive_task_path_reacquire = true
    })
end

M.TASK_DETAIL_RECOVERY_CONFIGS = {
    {
        key = "double_strings_after_upper_prefer_lower_detail",
        task_patterns = {
            "双弦"
        },
        prefer_detail_patterns = {
            "击败下弦之默吉特",
            "击败下弦"
        },
        raw_detail_patterns = {
            "完成",
            ""
        }
    }
}

M.TASK_NAME_CONFIGS = {
    ["与日争辉 / 觐见女王"] = {
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "觐见女王"
        },
        constraint_mode = "all",
        allow_wait_task_path_route_action_recover = true
    },
    ["与日争辉 / 挑战大竞技场"] = make_daylight_rivalry_grand_arena_kite_task_config(),
    ["与日争辉 / 击败“太阳冠军”杰拉尔德"] = make_daylight_rivalry_grand_arena_kite_task_config(),
    ["与日争辉"] = make_sun_faction_join_dialogue_flow_task(),
    ["尽快找到丽芙"] = {
        objective = {
            key = "old_dusk_find_liv_dialogue_endpoint",
            followup_route_action_key = "old_dusk_liv_dialogue_5980_5640",
            trigger_distance = 420,
            ignore_terminal_text_change_when_objective_same = true
        },
        task_patterns = {
            "旧日的黄昏"
        },
        task_detail_patterns = {
            "尽快找到丽芙"
        },
        constraint_mode = "all"
    },
    ["与丽芙交谈"] = {
        objective = {
            key = "old_dusk_find_liv_dialogue_endpoint",
            followup_route_action_key = "old_dusk_liv_dialogue_5980_5640",
            trigger_distance = 420,
            ignore_terminal_text_change_when_objective_same = true
        },
        task_patterns = {
            "旧日的黄昏"
        },
        task_detail_patterns = {
            "与丽芙交谈"
        },
        constraint_mode = "all"
    },
    ["主线 龙陨之野 / 向科里询问情报"] = {
        task_patterns = {
            "龙陨之野"
        },
        task_detail_patterns = {
            "向科里询问情报"
        },
        constraint_mode = "all",
        allow_wait_task_path_route_action_recover = true
    },
    ["追击觉醒者莱安"] = {
        boss_objective_point_key = "old_dusk_lai_an_boss_room_14861_23378",
        task_patterns = {
            "旧日的黄昏"
        },
        task_detail_patterns = {
            "追击觉醒者莱安"
        },
        constraint_mode = "all"
    },
    ["\u{524D}\u{5F80}\u{96C4}\u{72EE}\u{4E4B}\u{5FC3}\u{FF0C}\u{53C2}\u{4E0E}\u{665A}\u{661F}\u{7684}\u{53CD}\u{653B}"] = {
        task_patterns = {
            "\u{7FA4}\u{661F}\u{4E4B}\u{8F89}"
        },
        task_detail_patterns = {
            "\u{524D}\u{5F80}\u{96C4}\u{72EE}\u{4E4B}\u{5FC3}\u{FF0C}\u{53C2}\u{4E0E}\u{665A}\u{661F}\u{7684}\u{53CD}\u{653B}"
        },
        constraint_mode = "all",
        objective = {
            key = "stars_glory_to_lionheart_transport",
            mode = "task_objective_button",
            trigger_distance = 320,
            skip_direct_interact = true,
            arm_task_entry_action_after_click = true,
            force_task_call_after_transition = true,
            task_pos_reject_extra_ms = 3500,
            ignore_terminal_text_change_when_objective_same = true,
            button_steps = {
                {
                    key = "stars_glory_lionheart_transport_btn",
                    label = "\u{96C4}\u{72EE}\u{4E4B}\u{5FC3}TransportBtn",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.TransportBtn",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.TransportBtn"
                    },
                    hint_client_x = 704.528564,
                    hint_client_y = 712.631531,
                    hint_ratio_x = 0.489256,
                    hint_ratio_y = 0.791813,
                    hint_max_distance = 100.000,
                    prefer_hint_fallback = true,
                    hover_capture_enabled = true,
                    hover_capture_client_left = 690.0,
                    hover_capture_client_top = 685.0,
                    hover_capture_client_right = 745.0,
                    hover_capture_client_bottom = 730.0,
                    hover_capture_retry_ms = 700,
                    settle_ms = 1800,
                    task_pos_reject_extra_ms = 3500
                }
            }
        },
        entry_action = {
            key = "stars_glory_lionheart_world_map_send",
            mode = "world_map_send",
            defer_until_explicit_arm = true,
            map_open_wait_ms = 3000,
            center_click_ratio_x = 0.503472,
            center_click_ratio_y = 0.365556,
            center_use_human_mouse = true,
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 18000,
            step = {
                label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "\u{4F20}\u{9001}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
                distance_min = 8.248740,
                distance_max = 9.248740,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
                },
                hint_client_x = 659.188843,
                hint_client_y = 827.716736,
                hint_ratio_x = 0.457770,
                hint_ratio_y = 0.919685,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true
            }
        }
    },
    ["\u{5F00}\u{542F}\u{7B2C}\u{4E00}\u{5EA7}\u{5723}\u{5149}\u{5854}"] = {
        objective = {
            key = "fallen_city_holy_tower_map_trap_sequence",
            mode = "task_objective_button",
            trigger_distance = 220,
            skip_direct_interact = true,
            force_task_call_after_transition = true,
            task_pos_reject_extra_ms = 3500,
            ignore_terminal_text_change_when_objective_same = true,
            button_steps = {
                {
                    key = "fallen_city_holy_tower_map_trap_btn",
                    label = "\u{5723}\u{5149}\u{5854}MapTrap\u{6309}\u{94AE}",
                    distance_anchor_exact_text = "\u{963F}\u{745E}\u{5A05}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                    distance_min = 168.199816,
                    distance_max = 173.199816,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                    },
                    hint_client_x = 704.204834,
                    hint_client_y = 727.439941,
                    hint_ratio_x = 0.489031,
                    hint_ratio_y = 0.808267,
                    hint_max_distance = 120.000,
                    prefer_hint_fallback = true,
                    settle_ms = 2200,
                    task_pos_reject_extra_ms = 3500
                }
            }
        }
    },
    ["\u{4E0A}\u{53E4}\u{6218}\u{573A} / \u{51FB}\u{8D25}\u{88AB}\u{64CD}\u{7EB5}\u{7684}\u{54E5}\u{5E03}\u{6797}"] = make_boss_kite_task_config(
        "ancient_battlefield_controlled_goblin_fixed_kite",
        {
            trigger_distance = 1200,
            immediate_kite_on_reached = true,
            kite_anchor_source = "task_destination",
            kite_radius = 1200,
            kite_point_count = 4,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            kite_points = {
                { x = 11651.07, y = -7607.71, z = 566.00 },
                { x = 12712.64, y = -7604.06, z = 609.00 },
                { x = 12792.69, y = -6343.92, z = 606.00 },
                { x = 11516.00, y = -6634.00, z = 609.31 }
            }
        },
        {
            task_patterns = { "\u{4E0A}\u{53E4}\u{6218}\u{573A}" },
            task_detail_patterns = { "\u{51FB}\u{8D25}\u{88AB}\u{64CD}\u{7EB5}\u{7684}\u{54E5}\u{5E03}\u{6797}" },
            constraint_mode = "all",
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            }
        }
    ),
    ["击败矮人王多加尔"] = {
        task_patterns = {
            "群山之心"
        },
        task_detail_patterns = {
            "击败矮人王多加尔"
        },
        constraint_mode = "all",
        boss_objective_point_key = "mountain_heart_dwarf_king_endpoint_kite",
        revive_reentry_objective_key = "mountain_heart_dwarf_king_endpoint_kite",
        revive_reentry = make_mountain_heart_dwarf_king_reentry_config()
    },
    ["\u{6467}\u{6BC1}\u{7B51}\u{5899}\u{70AE}"] = make_boss_kite_task_config(
        "wall_cannon_boss",
        {
            exit_kite_on_detail_missing = true,
            exit_kite_on_detail_missing_after_ms = 2500,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true
        },
        {
            approach_suppress_nearby_monster_pulse_goal_distance = 1200
        }
    ),
    ["\u{6467}\u{6BC1}\u{9A7B}\u{5899}\u{70AE}"] = make_boss_kite_task_config(
        "wall_cannon_boss",
        {
            exit_kite_on_detail_missing = true,
            exit_kite_on_detail_missing_after_ms = 2500,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true
        },
        {
            approach_suppress_nearby_monster_pulse_goal_distance = 1200
        }
    ),
    ["\u{89C9}\u{9192}\u{60E9}\u{7F5A}\u{8005}"] = make_boss_kite_task_config(
        "awakened_punisher_boss",
        {
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true
        }
    ),
    ["\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{60E9}\u{7F5A}\u{8005}"] = make_boss_kite_task_config(
        "awakened_punisher_boss",
        {
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true
        }
    ),
    ["\u{51FB}\u{6740}\u{89C9}\u{9192}\u{60E9}\u{7F5A}\u{8005}"] = make_boss_kite_task_config(
        "awakened_punisher_boss",
        {
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true
        }
    ),
    ["\u{6740}\u{6B7B}\u{89C9}\u{9192}\u{60E9}\u{7F5A}\u{8005}"] = make_boss_kite_task_config(
        "awakened_punisher_boss",
        {
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true
        }
    ),
    ["坠星 / 前往坠星集市"] = make_world_map_send_task_config(
        "falling_star_go_to_market_world_map_send",
        {
            label = "传送按钮",
            distance_anchor_exact_text = "传送",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1200,
            world_map_panel_missing_fallback_ms = 3500,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "坠星集市地图项按钮",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 428.011200,
                hint_client_y = 395.815979,
                hint_ratio_x = 0.297230,
                hint_ratio_y = 0.439796,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 472.00,
                fixed_fallback_client_y = 451.00,
                fixed_fallback_ratio_x = 0.327778,
                fixed_fallback_ratio_y = 0.501111,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 18000
        },
        {
            task_patterns = {
                "坠星"
            },
            task_detail_patterns = {
                "前往坠星集市"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            constraint_mode = "all",
            enable_linear_recipe = true
        }
    ),
    ["藏宝地：曙光大道 / 在群狼街巷找到下一个藏宝地（推荐等级27~35级）"] = make_world_map_send_task_config(
        "treasure_dawn_avenue_wolf_street_world_map_send",
        {
            label = "传送按钮",
            distance_anchor_exact_text = "传送",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 661.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.459159,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1200,
            center_click_ratio_x = 0.161223,
            center_click_ratio_y = 0.536667,
            center_use_human_mouse = true,
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 700,
            center_retry_ms = 1400,
            world_map_panel_missing_fallback_ms = 3500,
            transition_wait_ms = 2500,
            timeout_ms = 16000
        },
        {
            task_patterns = {
                "藏宝地：曙光大道"
            },
            task_detail_patterns = {
                "在群狼街巷找到下一个藏宝地（推荐等级27~35级）"
            },
            constraint_mode = "all"
        }
    ),
    ["\u{524D}\u{5F80}\u{9F99}\u{9AA8}\u{5C71}\u{810A}"] = make_world_map_send_task_config(
        "dragonbone_ridge_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 9.358734,
            distance_max = 10.358734,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 651.116943,
            hint_client_y = 816.849670,
            hint_ratio_x = 0.452165,
            hint_ratio_y = 0.907611,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            selection_step = {
                label = "\u{9F99}\u{9AA8}\u{5C71}\u{810A}\u{5730}\u{56FE}\u{9879}\u{6309}\u{94AE}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 686.359497,
                hint_client_y = 410.743622,
                hint_ratio_x = 0.476639,
                hint_ratio_y = 0.456382,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 724.00,
                fixed_fallback_client_y = 451.00,
                fixed_fallback_ratio_x = 0.502778,
                fixed_fallback_ratio_y = 0.501111,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            selection_settle_ms = 250,
            timeout_ms = 22000
        },
        {
            enable_linear_recipe = true
        }
    ),
    ["\u{524D}\u{5F80}\u{9F99}\u{9AA8}\u{5E73}\u{539F}"] = make_world_map_send_task_config(
        "dragonfall_dragonbone_plain_transport_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 9.358734,
            distance_max = 10.358734,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 651.116943,
            hint_client_y = 816.849670,
            hint_ratio_x = 0.452165,
            hint_ratio_y = 0.907611,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            defer_until_explicit_arm = true,
            map_open_wait_ms = 1200,
            center_click_ratio_x = 0.502432,
            center_click_ratio_y = 0.323333,
            center_use_human_mouse = true,
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 700,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000
        },
        {
            route_point_action_only = true,
            task_patterns = {
                "\u{9F99}\u{9668}\u{4E4B}\u{91CE}"
            },
            task_detail_patterns = {
                "\u{524D}\u{5F80}\u{9F99}\u{9AA8}\u{5E73}\u{539F}"
            },
            constraint_mode = "all"
        }
    ),
    ["\u{9F99}\u{9668}\u{4E4B}\u{91CE}"] = make_dragonbone_griffin_boss_task_config(),
    ["\u{6DF1}\u{5165}\u{9F99}\u{9AA8}\u{5C71}\u{810A}\u{8179}\u{5730}"] = make_dragonbone_griffin_boss_task_config(),
    ["\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}"] = make_dragonbone_griffin_boss_recovery_task_config(),
    ["\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}"] = make_dragonbone_griffin_boss_recovery_task_config(),
    ["\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"] = make_dragonbone_griffin_boss_recovery_task_config(),
    ["\u{5BFB}\u{627E}\u{77EE}\u{4EBA}\u{56FD}\u{5EA6}\u{5165}\u{53E3}"] = make_boss_kite_task_config(
        "plateau_dragonbone_beast_boss",
        {
            trigger_distance = 520,
            immediate_kite_on_reached = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            boss_clear_settle_ms = 2500,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            revive_reentry = make_plateau_dragonbone_beast_reentry_config()
        },
        {
            task_patterns = {
                "\u{9F99}\u{9668}\u{4E4B}\u{91CE}"
            },
            task_detail_patterns = {
                "\u{5BFB}\u{627E}\u{77EE}\u{4EBA}\u{56FD}\u{5EA6}\u{5165}\u{53E3}",
                "\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{5730}\u{884C}\u{5F02}\u{517D}"
            },
            constraint_mode = "all"
        }
    ),
    ["\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{5730}\u{884C}\u{5F02}\u{517D}"] = make_boss_kite_task_config(
        "plateau_dragonbone_beast_boss",
        {
            trigger_distance = 520,
            immediate_kite_on_reached = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            boss_clear_settle_ms = 2500,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            revive_reentry = make_plateau_dragonbone_beast_reentry_config()
        },
        {
            task_patterns = {
                "\u{9F99}\u{9668}\u{4E4B}\u{91CE}"
            },
            task_detail_patterns = {
                "\u{5BFB}\u{627E}\u{77EE}\u{4EBA}\u{56FD}\u{5EA6}\u{5165}\u{53E3}",
                "\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{5730}\u{884C}\u{5F02}\u{517D}"
            },
            constraint_mode = "all"
        }
    ),
    ["\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{5F02}\u{517D}"] = make_boss_kite_task_config(
        "plateau_dragonbone_beast_boss",
        {
            trigger_distance = 420,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            revive_reentry = make_plateau_dragonbone_beast_reentry_config()
        }
    ),
    ["\u{524D}\u{5F80}\u{5931}\u{843D}\u{77FF}\u{9053}\u{6DF1}\u{5904}"] = make_boss_kite_task_config(
        "lost_mine_depth_boss_room",
        {
            trigger_distance = 420,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true
        }
    ),
    ["\u{51FB}\u{8D25}\u{5730}\u{9B54}\u{9996}\u{9886}"] = make_boss_kite_task_config(
        "lost_mine_depth_boss_room",
        {
            trigger_distance = 420,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true
        }
    ),
    ["\u{6253}\u{8D25}\u{201C}\u{5934}\u{72FC}\u{201D}\u{9053}\u{5947}"] = make_empire_ashes_head_wolf_dodge_boss_task_config(),
    ["\u{5BFB}\u{627E}\u{7FA4}\u{72FC}\u{5E2E}\u{9996}\u{9886}"] = make_empire_ashes_head_wolf_dodge_boss_task_config(),
    ["\u{81EA}\u{7531}\u{7684}\u{7130}\u{706B}"] = make_free_fire_elsa_approval_boss_task_config(),
    ["\u{53D6}\u{5F97}\u{4F0A}\u{5C14}\u{838E}\u{7684}\u{8BA4}\u{53EF}"] = make_free_fire_elsa_approval_boss_task_config(),
    ["\u{94F6}\u{7130}\u{9996}\u{9886}\u{00B7}\u{4F0A}\u{5C14}\u{838E}"] = make_free_fire_elsa_approval_boss_task_config(),
    ["\u{4ECE}\u{957F}\u{8BA1}\u{8BAE}"] = make_long_plan_elsa_intel_dialogue_task_config(),
    ["\u{548C}\u{4F0A}\u{5C14}\u{838E}\u{4EA4}\u{6D41}\u{60C5}\u{62A5}"] = make_long_plan_elsa_intel_dialogue_task_config(),
    ["\u{8FD4}\u{56DE}\u{9ECE}\u{660E}\u{5723}\u{6240}"] = make_long_plan_elsa_intel_dialogue_task_config(),
    ["\u{53E6}\u{4E00}\u{4E2A}\u{9B54}\u{6CD5}\u{5B66}\u{9662}"] = make_another_magic_academy_forbidden_guard_boss_task_config(),
    ["\u{8E48}\u{706B}\u{4E4B}\u{4EBA} / \u{524D}\u{5F80}\u{5723}\u{5FB7}\u{5170}\u{9B54}\u{6CD5}\u{5B66}\u{9662}"] = make_world_map_send_task_config(
        "fire_treader_saint_delane_academy_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 659.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.457770,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1900,
            map_open_wait_jitter_ms = 600,
            center_click_ratio_x = 0.504167,
            center_click_ratio_y = 0.502222,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "\u{5723}\u{5FB7}\u{5170}\u{9B54}\u{6CD5}\u{5B66}\u{9662}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "\u{5723}\u{5FB7}\u{5170}\u{9B54}\u{6CD5}\u{5B66}\u{9662}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                distance_min = 168.785836,
                distance_max = 173.785836,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 683.011169,
                hint_client_y = 397.815979,
                hint_ratio_x = 0.474313,
                hint_ratio_y = 0.442018,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 726.00,
                fixed_fallback_client_y = 452.00,
                fixed_fallback_ratio_x = 0.504167,
                fixed_fallback_ratio_y = 0.502222,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 700,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000,
            defer_revive_during_map_entry = true
        },
        {
            task_names = {
                "\u{8E48}\u{706B}\u{4E4B}\u{4EBA}",
                "\u{4E3B}\u{7EBF} \u{8E48}\u{706B}\u{4E4B}\u{4EBA}"
            },
            task_detail_names = {
                "\u{524D}\u{5F80}\u{5723}\u{5FB7}\u{5170}\u{9B54}\u{6CD5}\u{5B66}\u{9662}"
            },
            constraint_mode = "all"
        }
    ),
    ["\u{8E48}\u{706B}\u{4E4B}\u{4EBA}"] = make_fire_treader_romel_boss_task_config(),
    ["\u{51FB}\u{8D25}\u{88AB}\u{9B54}\u{6CD5}\u{5B66}\u{9662}\u{6539}\u{9020}\u{7684}\u{7F57}\u{6885}\u{5C14}"] = make_fire_treader_romel_boss_task_config(),
    ["\u{5B9E}\u{9A8C}\u{4F53}\u{00B7}\u{7F57}\u{6885}\u{5C14}"] = make_fire_treader_romel_boss_task_config(),
    ["\u{738B}\u{56FD}\u{7EC8}\u{9014} / \u{51FB}\u{8D25}\u{7070}\u{70EC}\u{673A}\u{7532}"] = make_kingdom_end_deep_boss_task_config(),
    ["\u{4E3B}\u{7EBF} \u{738B}\u{56FD}\u{7EC8}\u{9014} / \u{51FB}\u{8D25}\u{7070}\u{70EC}\u{673A}\u{7532}"] = make_kingdom_end_deep_boss_task_config(),
    ["灾厄将至 / 继续追击莱安"] = make_wasteland_path_longhorn_task_config(),
    ["主线 灾厄将至 / 继续追击莱安"] = make_wasteland_path_longhorn_task_config(),
    ["\u{51FB}\u{8D25}\u{7279}\u{6B8A}\u{5B9E}\u{9A8C}\u{4F53}\u{57FA}\u{5C14}"] = make_forgotten_temple_special_experiment_keel_task_config(),
    ["\u{7279}\u{6B8A}\u{5B9E}\u{9A8C}\u{4F53}\u{57FA}\u{5C14}"] = make_forgotten_temple_special_experiment_keel_task_config(),
    ["坠星 / 前往集市中心"] = make_boss_kite_task_config(
        "falling_star_market_center_route_kite",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            seamless_kite = true,
            kite_switch_ms = 2000,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            kite_points = {
                { x = -372.02, y = -171.76, z = 2026.00 },
                { x = -17.34, y = 219.02, z = 2032.84 },
                { x = 538.66, y = 364.33, z = 2046.76 },
                { x = 971.16, y = 171.65, z = 2057.52 },
                { x = 1236.11, y = -309.72, z = 2040.21 },
                { x = 1188.95, y = -782.28, z = 2035.18 },
                { x = 918.32, y = -1162.18, z = 2028.86 },
                { x = 303.64, y = -1254.36, z = 2015.21 },
                { x = -277.20, y = -1163.33, z = 2023.37 }
            }
        },
        {
            task_patterns = {
                "坠星"
            },
            task_detail_patterns = {
                "前往集市中心"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    ),
    ["坠星 / 击退伊吉王军，解放坠星集市"] = make_boss_kite_task_config(
        "falling_star_liberate_market_route_kite",
        {
            trigger_distance = 1200,
            immediate_kite_on_reached = true,
            seamless_kite = true,
            kite_switch_ms = 2000,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            kite_points = {
                { x = -310.62, y = -643.57, z = 2017.94 },
                { x = 912.80, y = -1200.78, z = 2028.81 },
                { x = -271.99, y = -1793.04, z = 2015.48 }
            }
        },
        {
            task_patterns = {
                "坠星"
            },
            task_detail_patterns = {
                "击退伊吉王军，解放坠星集市"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    ),
    ["晚星待明 / 跟随艾丝梅拉达，前往营救晚星战俘"] = make_late_star_royal_encirclement_boss_task_config({
        detail_pattern = "跟随艾丝梅拉达，前往营救晚星战俘"
    }),
    ["晚星待明 / 与艾丝梅拉达对话"] = {
        task_patterns = {
            "晚星待明"
        },
        task_detail_patterns = {
            "与艾丝梅拉达对话"
        },
        constraint_mode = "all",
        allow_wait_task_path_route_action_recover = true
    },
    ["晚星待明 / 离开坠星集市，前往伊吉部族夺回火种"] = {
        task_patterns = {
            "晚星待明"
        },
        task_detail_patterns = {
            "离开坠星集市，前往伊吉部族夺回火种"
        },
        constraint_mode = "all",
        allow_wait_task_path_route_action_recover = true
    },
    ["晚星领袖 / 与艾丝梅拉达对话"] = {
        task_patterns = {
            "晚星领袖"
        },
        task_detail_patterns = {
            "与艾丝梅拉达对话"
        },
        constraint_mode = "all",
        allow_wait_task_path_route_action_recover = true
    },
    ["晚星待明 / 击败王命围捕"] = make_late_star_royal_encirclement_boss_task_config({
        detail_pattern = "击败王命围捕",
        allow_no_target = true
    }),
    ["突破重围 / 带领反抗军突破重围"] = make_breakthrough_royal_message_boss_task_config({
        key = "breakthrough_lead_rebels_endpoint_kite",
        detail_pattern = "带领反抗军突破重围"
    }),
    ["突破重围 / 击败王命传讯"] = make_breakthrough_royal_message_boss_task_config({
        key = "breakthrough_defeat_royal_message_no_target_kite",
        detail_pattern = "击败王命传讯",
        allow_no_target = true
    }),
    ["遮风壁垒 / 进入伊吉部族，打听狮心王的下落"] = make_windbreak_barrier_royal_hunt_boss_task_config({
        detail_pattern = "进入伊吉部族，打听狮心王的下落"
    }),
    ["遮风壁垒 / 击败王命巡猎"] = make_windbreak_barrier_royal_hunt_boss_task_config({
        detail_pattern = "击败王命巡猎",
        allow_no_target = true
    }),
    ["狮心 / 击败狮心王"] = make_boss_kite_task_config(
        "lionheart_defeat_lionheart_king_boss_kite",
        {
            trigger_distance = 700,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true
        },
        {
            task_patterns = {
                "狮心"
            },
            task_detail_patterns = {
                "击败狮心王"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    ),
    ["永夜鸣沙 / 向沙漠深处进发，找到伊吉人的聚集地"] = make_boss_kite_task_config(
        "eternal_sand_find_iji_gathering_kite",
        {
            trigger_distance = 520,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 800,
            nearby_text_task_change_confirm_count = 2,
            nearby_text_task_change_exit_patterns = {
                "前往沉没沙丘"
            },
            ignore_terminal_text_change_when_objective_same = true
        },
        {
            task_patterns = {
                "永夜鸣沙"
            },
            task_detail_patterns = {
                "向沙漠深处进发，找到伊吉人的聚集地"
            },
            constraint_mode = "all"
        }
    ),
    ["\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"] = make_abyss_below_awakened_temple_deep_route_task_config(),
    ["\u{8FDB}\u{5165}\u{89C9}\u{9192}\u{79D8}\u{6BBF}\u{6DF1}\u{5904}"] = make_abyss_below_awakened_temple_deep_route_task_config(),
    ["\u{8BE2}\u{95EE}\u{4E3D}\u{8299}\u{60C5}\u{51B5}"] = make_overcast_city_ask_liv_key_sequence_task_config(),
    ["\u{9634}\u{4E91}\u{538B}\u{57CE}"] = make_overcast_city_awakened_ryan_task_config(),
    ["\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}"] = make_overcast_city_awakened_ryan_task_config(),
    ["\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}"] = make_overcast_city_awakened_ryan_task_config(),
    ["\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}"] = make_overcast_city_awakened_ryan_task_config(),
    ["\u{65C5}\u{9014}\u{4E4B}\u{59CB}"] = make_journey_begin_awakened_leader_task_config(),
    ["\u{7A81}\u{7834}\u{89C9}\u{9192}\u{8005}\u{91CD}\u{56F4}"] = make_journey_begin_awakened_leader_task_config(),
    ["\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}"] = make_journey_begin_awakened_leader_task_config(),
    ["\u{4E0A}\u{53E4}\u{6218}\u{573A} / \u{5E26}\u{4E0A}\u{79D1}\u{91CC}\u{FF0C}\u{7EE7}\u{7EED}\u{8FFD}\u{8E2A}\u{83B1}\u{5B89}"] = make_ancient_battlefield_trace_ryan_task_config(),
    ["\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{526F}\u{5B98}"] = make_fanmu_blocking_deputy_task_config(),
    ["\u{9003}\u{79BB}\u{5185}\u{57CE}\u{533A} / \u{524D}\u{5F80}\u{5B66}\u{8005}\u{8857}\u{5DF7}\u{7684}\u{51FA}\u{53E3}"] = make_escape_inner_city_exit_hass_kite_task_config({
        "\u{524D}\u{5F80}\u{5B66}\u{8005}\u{8857}\u{5DF7}\u{7684}\u{51FA}\u{53E3}"
    }),
    ["\u{9003}\u{79BB}\u{5185}\u{57CE}\u{533A} / \u{51FB}\u{8D25}\u{526F}\u{5B98}\u{54C8}\u{65AF}"] = make_escape_inner_city_exit_hass_kite_task_config({
        "\u{51FB}\u{8D25}\u{526F}\u{5B98}\u{54C8}\u{65AF}"
    }, {
        no_task_target = true
    }),
    ["\u{51FB}\u{8D25}\u{9A7B}\u{5B88}\u{57CE}\u{5899}\u{7684}\u{5B66}\u{57CE}\u{5B88}\u{536B}"] = make_wall_of_sighs_city_guard_task_config(),
    ["\u{7EE7}\u{7EED}\u{524D}\u{8FDB}\u{FF0C}\u{7A7F}\u{8D8A}\u{610F}\u{5FD7}\u{9AD8}\u{5899}"] = make_wall_of_sighs_will_wall_boss_task_config(),
    ["\u{53F9}\u{606F}\u{4E4B}\u{5899} / \u{548C}\u{963F}\u{745E}\u{5A05}\u{4EA4}\u{8C08}"] = {
        task_patterns = {
            "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
        },
        task_detail_patterns = {
            "\u{548C}\u{963F}\u{745E}\u{5A05}\u{4EA4}\u{8C08}"
        },
        constraint_mode = "all",
        post_dialogue_flow = {
            key = "wall_of_sighs_aria_dialogue_after_jump_19797_-2126",
            initial_delay_ms = 900,
            timeout_ms = 9000,
            followup_route_action_key = "wall_of_sighs_aria_dialogue_after_jump_route_19797_-2126",
            followup_route_action_source = "wall_of_sighs_aria_dialogue_jump_followup",
            followup_route_action_ignore_retry = true
        }
    },
    ["\u{53F9}\u{606F}\u{4E4B}\u{5899} / \u{6D88}\u{706D}\u{5B88}\u{536B}\u{FF0C}\u{5173}\u{95ED}\u{6700}\u{7EC8}\u{673A}\u{5173}"] = make_wall_of_sighs_final_mechanism_task_config(),
    ["\u{5E2E}\u{52A9}\u{9A6C}\u{5FB7}\u{5170}\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{9996}\u{9886}"] = make_holy_fire_roadblock_awakened_task_config(
        "\u{5E2E}\u{52A9}\u{9A6C}\u{5FB7}\u{5170}\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{9996}\u{9886}"
    ),
    ["\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{89C9}\u{9192}\u{8005}"] = make_holy_fire_roadblock_awakened_task_config(
        "\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{89C9}\u{9192}\u{8005}"
    ),
    ["\u{7EE7}\u{7EED}\u{524D}\u{8FDB}\u{FF0C}\u{7A7F}\u{8D8A}\u{5DE8}\u{5899}"] = make_tianqian_cross_wall_task_config(),
    ["\u{51FB}\u{8D25}\u{5B88}\u{62A4}\u{5DE8}\u{70AE}\u{7684}\u{89C9}\u{9192}\u{8005}"] = make_tianqian_guard_cannon_awakened_task_config(),
    ["\u{53CD}\u{51FB}\u{7684}\u{9ECE}\u{660E}"] = make_boss_kite_task_config(
        "counterattack_dawn_worm_room_17190_16840",
        {
            trigger_distance = 1400,
            immediate_kite_on_reached = true,
            seamless_kite = true,
            kite_switch_ms = 2400,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            kite_points = {
                { x = 19717.21, y = 20873.74, z = 920.00 },
                { x = 19097.61, y = 19766.45, z = 920.00 },
                { x = 18291.47, y = 20520.47, z = 920.00 }
            }
        },
        {
            task_patterns = { "\u{53CD}\u{51FB}\u{7684}\u{9ECE}\u{660E}" },
            task_detail_patterns = { "\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{8815}\u{866B}" },
            constraint_mode = "all"
        }
    ),
    ["\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{8815}\u{866B}"] = make_boss_kite_task_config(
        "counterattack_dawn_worm_room_17190_16840",
        {
            trigger_distance = 1400,
            immediate_kite_on_reached = true,
            seamless_kite = true,
            kite_switch_ms = 2400,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            kite_points = {
                { x = 19717.21, y = 20873.74, z = 920.00 },
                { x = 19097.61, y = 19766.45, z = 920.00 },
                { x = 18291.47, y = 20520.47, z = 920.00 }
            }
        },
        {
            task_patterns = { "\u{53CD}\u{51FB}\u{7684}\u{9ECE}\u{660E}" },
            task_detail_patterns = { "\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{8815}\u{866B}" },
            constraint_mode = "all"
        }
    ),
    ["\u{51FB}\u{8D25}\u{5F02}\u{5316}\u{7684}\u{4E3D}\u{8299}"] = make_boss_kite_task_config(
        "shadowland_mutated_liv_boss_room",
        {
            trigger_distance = 420,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true
        },
        {
            task_patterns = { "\u{71C3}\u{70E7}\u{7684}\u{957F}\u{591C}" },
            task_detail_patterns = { "\u{51FB}\u{8D25}\u{5F02}\u{5316}\u{7684}\u{4E3D}\u{8299}" },
            constraint_mode = "all"
        }
    ),
    ["追寻莱安的踪迹"] = make_boss_kite_task_config(
        "abyss_below_ryan_phantom_boss_room",
        {
            trigger_distance = 1600,
            immediate_kite_on_reached = true,
            seamless_kite = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2400,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true
        },
        {
            task_patterns = { "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}" },
            task_detail_patterns = { "追寻莱安的踪迹" },
            constraint_mode = "all",
            exclude_task_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            },
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            }
        }
    ),
    ["\u{51FB}\u{8D25}\u{83B1}\u{5B89}\u{5E7B}\u{5F71}"] = make_boss_kite_task_config(
        "abyss_below_ryan_phantom_boss_room",
        {
            trigger_distance = 1600,
            immediate_kite_on_reached = true,
            seamless_kite = true,
            kite_switch_ms = 2400,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            no_task_target_kite_wait_ms = 900,
            no_task_target_ignore_path_wait = true,
            kite_points = {
                { x = -1549.53, y = 25388.33, z = 503.00 },
                { x = -116.72, y = 24092.46, z = 503.00 },
                { x = -2038.89, y = 24231.98, z = 503.00 }
            }
        },
        {
            task_patterns = { "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}" },
            task_detail_patterns = { "\u{51FB}\u{8D25}\u{83B1}\u{5B89}\u{5E7B}\u{5F71}" },
            constraint_mode = "all",
            exclude_task_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            },
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            }
        }
    ),
    ["\u{8FDB}\u{5165}\u{5347}\u{534E}\u{79D8}\u{6BBF}\u{FF0C}\u{963B}\u{6B62}\u{57FA}\u{5188}"] = {
        objective = {
            key = "day_of_apotheosis_enter_ascension_hall_transport",
            mode = "task_objective_button",
            trigger_distance = 320,
            skip_direct_interact = true,
            ignore_terminal_text_change_when_objective_same = true,
            button_steps = {
                {
                    key = "day_of_apotheosis_enter_ascension_hall_transport_btn",
                    label = "\u{5347}\u{534E}\u{79D8}\u{6BBF}TransportBtn",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.TransportBtn",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.TransportBtn"
                    },
                    hint_client_x = 718.204834,
                    hint_client_y = 741.439941,
                    hint_ratio_x = 0.498753,
                    hint_ratio_y = 0.823822,
                    hint_max_distance = 100.000,
                    prefer_hint_fallback = true,
                    settle_ms = 1200
                }
            }
        },
        task_patterns = { "\u{6210}\u{795E}\u{4E4B}\u{65E5}" },
        task_detail_patterns = { "\u{8FDB}\u{5165}\u{5347}\u{534E}\u{79D8}\u{6BBF}\u{FF0C}\u{963B}\u{6B62}\u{57FA}\u{5188}" },
        constraint_mode = "all"
    },
    ["\u{51FB}\u{8D25}\u{5438}\u{6536}\u{4E86}\u{707E}\u{70EC}\u{548C}\u{706B}\u{79CD}\u{529B}\u{91CF}\u{7684}\u{57FA}\u{5188}"] = make_boss_kite_task_config(
        "day_of_apotheosis_absorbed_geegang_boss_room",
        {
            trigger_distance = 1700,
            immediate_kite_on_reached = true,
            seamless_kite = true,
            kite_switch_ms = 2400,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_no_task_target_force_kite = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 800,
            nearby_text_task_change_confirm_count = 2,
            nearby_text_task_change_exit_patterns = {
                "\u{8BE2}\u{95EE}\u{963F}\u{745E}\u{5A05}\u{60C5}\u{51B5}"
            },
            revive_reentry = make_revive_reentry_config({
                key = "day_of_apotheosis_geegang_boss_room_reentry_291_-126",
                label = "\u{6210}\u{795E}\u{4E4B}\u{65E5}\u{57FA}\u{5188}Boss\u{91CD}\u{8FDB}\u{623F}",
                anchor = {
                    x = 290.58,
                    y = -125.95,
                    z = 551.90,
                    radius = 620,
                    z_tolerance = 360
                },
                interact_distance = 300,
                portal_scan_distance = 900,
                retry_ms = 900,
                settle_ms = 1400,
                timeout_ms = 22000,
                post_transition_boss_engage_ms = 16000,
                fallback_interact = true
            }),
            kite_points = {
                { x = 716.00, y = 2728.00, z = 1281.00 },
                { x = 7.00, y = 2732.00, z = 1281.00 },
                { x = 322.00, y = 2089.00, z = 1281.00 }
            }
        },
        {
            task_patterns = { "\u{6210}\u{795E}\u{4E4B}\u{65E5}" },
            task_detail_patterns = { "\u{51FB}\u{8D25}\u{5438}\u{6536}\u{4E86}\u{707E}\u{70EC}\u{548C}\u{706B}\u{79CD}\u{529B}\u{91CF}\u{7684}\u{57FA}\u{5188}" },
            constraint_mode = "all",
            exclude_task_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            },
            exclude_task_detail_patterns = {
                "\u{4EA4}\u{8C08}",
                "\u{5BF9}\u{8BDD}"
            }
        }
    ),
    ["\u{53D6}\u{56DE}\u{6765}\u{4E4B}\u{4E0D}\u{6613}\u{7684}\u{706B}\u{79CD}"] = {
        task_patterns = {
            "\u{71C3}\u{70E7}\u{7684}\u{957F}\u{591C}"
        },
        task_detail_patterns = {
            "\u{53D6}\u{56DE}\u{6765}\u{4E4B}\u{4E0D}\u{6613}\u{7684}\u{706B}\u{79CD}"
        },
        constraint_mode = "all"
    },
    ["\u{524D}\u{5F80}\u{4F59}\u{70EC}\u{4E4B}\u{606F}"] = make_world_map_send_task_config(
        "ember_rest_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 659.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.457770,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1900,
            center_click_ratio_x = 0.503822,
            center_click_ratio_y = 0.495556,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "\u{907F}\u{96BE}\u{6240}-\u{4F59}\u{70EC}\u{4E4B}\u{606F}\u{6309}\u{94AE}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 676.011230,
                hint_client_y = 395.815979,
                hint_ratio_x = 0.469452,
                hint_ratio_y = 0.439796,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 725.00,
                fixed_fallback_client_y = 444.00,
                fixed_fallback_ratio_x = 0.503472,
                fixed_fallback_ratio_y = 0.493333,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            failure_route_action_key = "wall_of_sighs_forbidden_wall_no_path_route_-16603_9004",
            timeout_ms = 16000
        },
        {
            -- This exact-detail key is used when the game returns no task path and opens world map selection.
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            enable_linear_recipe = true
        }
    ),
    ["\u{524D}\u{5F80}\u{6C89}\u{6CA1}\u{6C99}\u{4E18}"] = make_world_map_send_task_config(
        "sunken_dunes_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 9.358734,
            distance_max = 10.358734,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 651.116943,
            hint_client_y = 816.849670,
            hint_ratio_x = 0.452165,
            hint_ratio_y = 0.907611,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true,
            hover_capture_client_left = 654,
            hover_capture_client_top = 789,
            hover_capture_client_right = 790,
            hover_capture_client_bottom = 810,
            hover_capture_retry_ms = 900
        },
        {
            map_open_wait_ms = 1900,
            center_click_ratio_x = 0.456250,
            center_click_ratio_y = 0.502222,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "\u{6C89}\u{6CA1}\u{6C99}\u{4E18}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "\u{6C89}\u{6CA1}\u{6C99}\u{4E18}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                distance_min = 143.092978,
                distance_max = 148.092978,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 622.359558,
                hint_client_y = 410.743591,
                hint_ratio_x = 0.432194,
                hint_ratio_y = 0.456382,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 657.00,
                fixed_fallback_client_y = 452.00,
                fixed_fallback_ratio_x = 0.456250,
                fixed_fallback_ratio_y = 0.502222,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 24000
        },
        {
            enable_linear_recipe = true,
            task_patterns = {
                "\u{6C38}\u{591C}\u{9E23}\u{6C99}"
            },
            task_detail_patterns = {
                "\u{524D}\u{5F80}\u{6C89}\u{6CA1}\u{6C99}\u{4E18}"
            },
            constraint_mode = "all"
        }
    ),
    ["长夜明星 / 感谢伊吉女孩的相助"] = {
        task_patterns = {
            "长夜明星"
        },
        task_detail_patterns = {
            "感谢伊吉女孩的相助"
        },
        constraint_mode = "all",
        allow_wait_task_path_route_action_recover = true
    },
    ["长夜明星 / 与艾丝一同前往坠星集市"] = {
        task_patterns = {
            "长夜明星"
        },
        task_detail_patterns = {
            "与艾丝一同前往坠星集市"
        },
        constraint_mode = "all",
        allow_wait_task_path_route_action_recover = true
    },
    ["狂沙地海 / 前往女神裙摆"] = make_world_map_send_task_config(
        "sand_sea_go_to_goddess_hem_world_map_send",
        {
            label = "传送按钮",
            distance_anchor_exact_text = "传送",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1200,
            world_map_panel_missing_fallback_ms = 3500,
            center_click_ratio_x = 0.502778,
            center_click_ratio_y = 0.502222,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "女神裙摆地图项按钮",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 676.011230,
                hint_client_y = 396.815979,
                hint_ratio_x = 0.469452,
                hint_ratio_y = 0.440907,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 724.00,
                fixed_fallback_client_y = 452.00,
                fixed_fallback_ratio_x = 0.502778,
                fixed_fallback_ratio_y = 0.502222,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 18000
        },
        {
            task_patterns = {
                "狂沙地海"
            },
            task_detail_patterns = {
                "前往女神裙摆"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            constraint_mode = "all",
            enable_linear_recipe = true
        }
    ),
    ["\u{5BFC}\u{5E08}\u{9988}\u{8D60}"] = make_dialogue_locator_flow_task_config(
        "mentor_gift_task_detail_before_antonio_jump",
        {
            {
                key = "mentor_gift_task_detail_btn",
                label = "[\u{4EFB}\u{52A1}] \u{5BFC}\u{5E08}\u{9988}\u{8D60}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "[\u{4EFB}\u{52A1}] \u{5BFC}\u{5E08}\u{9988}\u{8D60}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 71.799266,
                distance_max = 76.240458,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 586.211731,
                hint_client_y = 258.042725,
                hint_ratio_x = 0.407091,
                hint_ratio_y = 0.286714,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                retry_ms = 600,
                settle_ms = 1200,
            }
        },
        {
            key = "mentor_gift_task_detail_before_antonio_jump",
            timeout_ms = 9000,
            origins = {
                "npc",
                "interaction_prompt"
            },
            settle_ms = 1200
        },
        {
            task_patterns = { "\u{5BFC}\u{5E08}\u{9988}\u{8D60}" },
            task_detail_patterns = { "\u{4E0E}\u{5B89}\u{4E1C}\u{5C3C}\u{5965}\u{5B66}\u{8005}\u{5BF9}\u{8BDD}" },
            constraint_mode = "all",
            post_dialogue_flow = {
                key = "mentor_gift_wait_after_antonio_jump",
                wait_task_info_refresh_after_jump = true,
                task_info_refresh_timeout_ms = 6500
            }
        }
    ),
    ["\u{5BFC}\u{5E08}\u{9988}\u{8D60} / \u{4E0E}\u{4F59}\u{70EC}\u{4E4B}\u{606F}\u{7684}\u{6280}\u{80FD}\u{5BFC}\u{5E08}\u{5BF9}\u{8BDD}"] = make_dialogue_locator_flow_task_config(
        "mentor_gift_ember_skill_tutor_dragon_scale_belt_before_jump",
        {
            {
                key = "mentor_gift_head_wolf_greaves_btn",
                label = "\u{5934}\u{72FC}\u{62A4}\u{80EB}\u{6309}\u{94AE}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 593.211731,
                hint_client_y = 224.523193,
                hint_ratio_x = 0.411953,
                hint_ratio_y = 0.249470,
                hint_max_distance = 30.000,
                retry_ms = 600,
                settle_ms = 1200
            }
        },
        {
            key = "mentor_gift_ember_skill_tutor_dragon_scale_belt_before_jump",
            timeout_ms = 9000,
            origins = {
                "npc",
                "interaction_prompt"
            },
            settle_ms = 1200
        },
        {
            task_patterns = {
                "\u{5BFC}\u{5E08}\u{9988}\u{8D60}"
            },
            task_detail_patterns = {
                "\u{4E0E}\u{4F59}\u{70EC}\u{4E4B}\u{606F}\u{7684}\u{6280}\u{80FD}\u{5BFC}\u{5E08}\u{5BF9}\u{8BDD}",
                "\u{788E}\u{7532}\u{5DE8}\u{5251}"
            },
            constraint_mode = "all",
            post_dialogue_flow = {
                key = "mentor_gift_ember_skill_tutor_wait_after_dragon_scale_belt_jump",
                wait_task_info_refresh_after_jump = true,
                task_info_refresh_timeout_ms = 6500
            }
        }
    ),
    ["\u{88C5}\u{5907}\u{6253}\u{9020} / \u{4E0E}\u{4F59}\u{70EC}\u{4E4B}\u{606F}\u{7684}\u{519B}\u{706B}\u{5546}\u{4EBA}\u{5BF9}\u{8BDD}"] = make_dialogue_locator_flow_task_config(
        "equipment_crafting_ember_arms_dealer_dragon_scale_belt_before_jump",
        {
            {
                key = "equipment_crafting_head_wolf_greaves_btn",
                label = "\u{5934}\u{72FC}\u{62A4}\u{80EB}\u{6309}\u{94AE}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 593.211731,
                hint_client_y = 224.523193,
                hint_ratio_x = 0.411953,
                hint_ratio_y = 0.249470,
                hint_max_distance = 30.000,
                retry_ms = 600,
                settle_ms = 1200
            }
        },
        {
            key = "equipment_crafting_ember_arms_dealer_dragon_scale_belt_before_jump",
            timeout_ms = 9000,
            origins = {
                "npc",
                "interaction_prompt"
            },
            settle_ms = 1200
        },
        {
            task_patterns = {
                "\u{88C5}\u{5907}\u{6253}\u{9020}"
            },
            task_detail_patterns = {
                "\u{4E0E}\u{4F59}\u{70EC}\u{4E4B}\u{606F}\u{7684}\u{519B}\u{706B}\u{5546}\u{4EBA}\u{5BF9}\u{8BDD}"
            },
            constraint_mode = "all",
            post_dialogue_flow = {
                key = "equipment_crafting_ember_arms_dealer_wait_after_dragon_scale_belt_jump",
                wait_task_info_refresh_after_jump = true,
                task_info_refresh_timeout_ms = 6500
            }
        }
    ),
    ["\u{5F02}\u{754C}\u{63A2}\u{7D22} / \u{4E0E}\u{4F59}\u{70EC}\u{4E4B}\u{606F}\u{7684}\u{5F02}\u{754C}\u{5BFC}\u{5E08}\u{5BF9}\u{8BDD}"] = make_dialogue_locator_flow_task_config(
        "otherworld_exploration_ember_otherworld_tutor_dragon_scale_belt_before_jump",
        {
            {
                key = "otherworld_exploration_head_wolf_greaves_btn",
                label = "\u{5934}\u{72FC}\u{62A4}\u{80EB}\u{6309}\u{94AE}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 593.211731,
                hint_client_y = 224.523193,
                hint_ratio_x = 0.411953,
                hint_ratio_y = 0.249470,
                hint_max_distance = 30.000,
                retry_ms = 600,
                settle_ms = 1200
            }
        },
        {
            key = "otherworld_exploration_ember_otherworld_tutor_dragon_scale_belt_before_jump",
            timeout_ms = 9000,
            origins = {
                "npc",
                "interaction_prompt"
            },
            settle_ms = 1200
        },
        {
            task_patterns = {
                "\u{5F02}\u{754C}\u{63A2}\u{7D22}"
            },
            task_detail_patterns = {
                "\u{4E0E}\u{4F59}\u{70EC}\u{4E4B}\u{606F}\u{7684}\u{5F02}\u{754C}\u{5BFC}\u{5E08}\u{5BF9}\u{8BDD}"
            },
            constraint_mode = "all",
            post_dialogue_flow = {
                key = "otherworld_exploration_ember_otherworld_tutor_wait_after_dragon_scale_belt_jump",
                wait_task_info_refresh_after_jump = true,
                task_info_refresh_timeout_ms = 6500
            }
        }
    ),
    ["\u{5723}\u{6D01}\u{4E4B}\u{706B} / \u{5C1D}\u{8BD5}\u{548C}\u{5B66}\u{57CE}\u{5B88}\u{536B}\u{4EA4}\u{8C08}"] = make_dialogue_locator_flow_task_config(
        "holy_fire_guard_dialogue_flow",
        {
            {
                key = "holy_fire_task_detail_btn",
                label = "[\u{4EFB}\u{52A1}] \u{5723}\u{6D01}\u{4E4B}\u{706B}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "[\u{4EFB}\u{52A1}] \u{5723}\u{6D01}\u{4E4B}\u{706B}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 71.799268,
                distance_max = 76.240468,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 586.211731,
                hint_client_y = 323.490753,
                hint_ratio_x = 0.407091,
                hint_ratio_y = 0.359434,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                poll_retry_count = 3,
                poll_interval_ms = 120,
                retry_ms = 500,
                settle_ms = 900
            }
        },
        {
            key = "holy_fire_guard_dialogue_flow",
            timeout_ms = 8000,
            origins = {
                "interaction_prompt"
            }
        },
        {
            task_patterns = { "\u{5723}\u{6D01}\u{4E4B}\u{706B}" },
            task_detail_patterns = { "\u{5C1D}\u{8BD5}\u{548C}\u{5B66}\u{57CE}\u{5B88}\u{536B}\u{4EA4}\u{8C08}" },
            constraint_mode = "all",
            post_dialogue_flow = {
                key = "holy_fire_guard_after_dialogue_click",
                mode = "after_jump_steps",
                steps = {
                    make_fixed_client_click_step({
                        key = "holy_fire_guard_after_dialogue_task_detail_btn",
                        label = "[\u{4EFB}\u{52A1}] \u{5723}\u{6D01}\u{4E4B}\u{706B}\u{5BF9}\u{8BDD}\u{540E}\u{6309}\u{94AE}",
                        fixed_client_x = 735.000000,
                        fixed_client_y = 349.000000,
                        fixed_ratio_x = 0.510417,
                        fixed_ratio_y = 0.387778,
                        settle_ms = 1200,
                        force_task_call_after_transition = false,
                        task_pos_reject_extra_ms = 2500
                    })
                },
                initial_delay_ms = 500,
                timeout_ms = 8000,
                arm_after_objective_button = true,
                skip_dialogue_jump = true
            }
        }
    ),
    ["\u{5723}\u{6D01}\u{4E4B}\u{706B} / \u{8FDB}\u{5165}\u{5B66}\u{57CE}\u{6DF1}\u{5904}"] = {
        task_patterns = { "\u{5723}\u{6D01}\u{4E4B}\u{706B}" },
        task_detail_patterns = { "\u{8FDB}\u{5165}\u{5B66}\u{57CE}\u{6DF1}\u{5904}" },
        constraint_mode = "all",
        allow_wait_task_path_route_action_recover = true
    },
    ["\u{957F}\u{591C}\u{7EC8}\u{5C3D} / \u{4E0E}\u{83AB}\u{7433}\u{5A1C}\u{4EA4}\u{8C08}"] = make_dialogue_locator_flow_task_config(
        "long_night_end_task_detail_before_molina_jump",
        {
            {
                key = "long_night_end_task_detail_btn",
                label = "[\u{4EFB}\u{52A1}] \u{957F}\u{591C}\u{7EC8}\u{5C3D}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "[\u{4EFB}\u{52A1}] \u{957F}\u{591C}\u{7EC8}\u{5C3D}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 71.799269,
                distance_max = 76.240461,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                strict_distance_anchor = true,
                hint_client_x = 586.211731,
                hint_client_y = 223.523193,
                hint_ratio_x = 0.407091,
                hint_ratio_y = 0.248359,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                poll_retry_count = 3,
                poll_interval_ms = 120,
                fixed_fallback_client_x = 730.00,
                fixed_fallback_client_y = 218.00,
                fixed_fallback_ratio_x = 0.506944,
                fixed_fallback_ratio_y = 0.242222,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80,
                retry_ms = 600,
                settle_ms = 1200
            }
        },
        {
            key = "long_night_end_task_detail_before_molina_jump",
            timeout_ms = 9000,
            origins = {
                "npc",
                "interaction_prompt"
            },
            settle_ms = 1200
        },
        {
            task_patterns = {
                "\u{957F}\u{591C}\u{7EC8}\u{5C3D}"
            },
            task_detail_patterns = {
                "\u{4E0E}\u{83AB}\u{7433}\u{5A1C}\u{4EA4}\u{8C08}"
            },
            constraint_mode = "all"
        }
    ),
    ["\u{5723}\u{8BEB}\u{4E4B}\u{672B} / \u{4E0E}\u{83AB}\u{7433}\u{5A1C}\u{4EA4}\u{8C08}"] = make_dialogue_locator_flow_task_config(
        "saint_end_molina_task_detail_before_jump",
        {
            {
                key = "saint_end_task_detail_btn",
                label = "[\u{4EFB}\u{52A1}] \u{5723}\u{8BEB}\u{4E4B}\u{672B}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "[\u{4EFB}\u{52A1}] \u{5723}\u{8BEB}\u{4E4B}\u{672B}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 71.799269,
                distance_max = 76.240461,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 586.211731,
                hint_client_y = 223.523193,
                hint_ratio_x = 0.407091,
                hint_ratio_y = 0.248359,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                poll_retry_count = 3,
                poll_interval_ms = 120,
                retry_ms = 600,
                settle_ms = 1200
            }
        },
        {
            key = "saint_end_molina_task_detail_before_jump",
            timeout_ms = 9000,
            origins = {
                "npc",
                "interaction_prompt"
            },
            settle_ms = 1200
        },
        {
            task_names = {
                "\u{5723}\u{8BEB}\u{4E4B}\u{672B}",
                "\u{4E3B}\u{7EBF} \u{5723}\u{8BEB}\u{4E4B}\u{672B}"
            },
            task_detail_names = {
                "\u{4E0E}\u{83AB}\u{7433}\u{5A1C}\u{4EA4}\u{8C08}"
            },
            constraint_mode = "all"
        }
    ),
    ["\u{79BB}\u{5F00}\u{5760}\u{661F}\u{96C6}\u{5E02}\u{FF0C}\u{524D}\u{5F80}\u{4F0A}\u{5409}\u{90E8}\u{65CF}\u{593A}\u{56DE}\u{706B}\u{79CD}"] = make_post_dialogue_flow_task_config(
        "evening_star_leave_market_wait_refresh_after_jump",
        {},
        {
            timeout_ms = 8000,
            wait_task_info_refresh_after_jump = true,
            task_info_refresh_timeout_ms = 12000
        },
        {
            task_patterns = {
                "\u{665A}\u{661F}\u{5F85}\u{660E}"
            },
            task_detail_patterns = {
                "\u{79BB}\u{5F00}\u{5760}\u{661F}\u{96C6}\u{5E02}\u{FF0C}\u{524D}\u{5F80}\u{4F0A}\u{5409}\u{90E8}\u{65CF}\u{593A}\u{56DE}\u{706B}\u{79CD}"
            },
            constraint_mode = "all"
        }
    ),
    ["\u{524D}\u{5F80}\u{906E}\u{98CE}\u{58C1}\u{969C}"] = make_world_map_send_task_config(
        "windbreak_barrier_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 659.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.457770,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1900,
            center_click_ratio_x = 0.462055,
            center_click_ratio_y = 0.345271,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "\u{906E}\u{98CE}\u{58C1}\u{969C}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "\u{906E}\u{98CE}\u{58C1}\u{969C}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                distance_min = 143.092981,
                distance_max = 148.092981,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 665.359497,
                hint_client_y = 310.743591,
                hint_ratio_x = 0.462055,
                hint_ratio_y = 0.345271,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 665.359497,
                fixed_fallback_client_y = 310.743591,
                fixed_fallback_ratio_x = 0.462055,
                fixed_fallback_ratio_y = 0.345271,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000
        },
        {
            task_patterns = {
                "\u{6C38}\u{591C}\u{4E4B}\u{98CE}"
            },
            task_detail_patterns = {
                "\u{524D}\u{5F80}\u{906E}\u{98CE}\u{58C1}\u{969C}"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            allow_wait_task_path_route_action_recover = true,
            constraint_mode = "all",
            enable_linear_recipe = true
        }
    ),
    ["\u{524D}\u{5F80}\u{4F0A}\u{5409}\u{805A}\u{843D}"] = make_world_map_send_task_config(
        "yiji_settlement_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 659.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.457770,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1900,
            center_click_ratio_x = 0.503472,
            center_click_ratio_y = 0.438889,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "\u{4F0A}\u{5409}\u{805A}\u{843D}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "\u{4F0A}\u{5409}\u{805A}\u{843D}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                distance_min = 143.092978,
                distance_max = 148.092978,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 685.359619,
                hint_client_y = 353.743591,
                hint_ratio_x = 0.475944,
                hint_ratio_y = 0.393048,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 723.00,
                fixed_fallback_client_y = 395.00,
                fixed_fallback_ratio_x = 0.502083,
                fixed_fallback_ratio_y = 0.438889,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000
        },
        {
            task_patterns = {
                "\u{4F0A}\u{5409}\u{90E8}\u{65CF}"
            },
            task_detail_patterns = {
                "\u{524D}\u{5F80}\u{4F0A}\u{5409}\u{805A}\u{843D}"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            constraint_mode = "all",
            enable_linear_recipe = true
        }
    ),
    ["前往预言圣地，追击抢走火种的神秘人"] = make_world_map_send_task_config(
        "trial_of_sun_yiji_settlement_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 659.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.457770,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1200,
            center_click_ratio_x = 0.503472,
            center_click_ratio_y = 0.384444,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "\u{9884}\u{8A00}\u{5723}\u{5730}\u{5730}\u{56FE}\u{6309}\u{94AE}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 687.359497,
                hint_client_y = 302.743561,
                hint_ratio_x = 0.477333,
                hint_ratio_y = 0.336382,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 725.00,
                fixed_fallback_client_y = 346.00,
                fixed_fallback_ratio_x = 0.503472,
                fixed_fallback_ratio_y = 0.384444,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000
        },
        {
            task_patterns = {
                "太阳的试炼"
            },
            task_detail_patterns = {
                "前往预言圣地，追击抢走火种的神秘人"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            constraint_mode = "all",
            enable_linear_recipe = true
        }
    ),
    ["进入太阳斗场"] = make_world_map_send_task_config(
        "enter_sun_arena_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 659.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.457770,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1200,
            center_click_ratio_x = 0.554167,
            center_click_ratio_y = 0.502222,
            center_use_human_mouse = true,
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000
        },
        {
            task_patterns = {
                "进入太阳斗场"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            enable_linear_recipe = true
        }
    ),
    ["前往永恒广场"] = make_world_map_send_task_config(
        "eternal_gilding_eternal_square_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 659.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.457770,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1200,
            center_click_ratio_x = 0.504167,
            center_click_ratio_y = 0.503333,
            center_use_human_mouse = true,
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000
        },
        {
            task_patterns = {
                "永恒鎏金"
            },
            task_detail_patterns = {
                "前往永恒广场"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            constraint_mode = "all",
            enable_linear_recipe = true
        }
    ),
    ["永恒鎏金 / 深入铺满纯金的广场，找到神秘人的下落"] = make_dialogue_locator_flow_task_config(
        "eternal_gilding_champion_road_task_detail_before_jump",
        {
            {
                key = "eternal_gilding_champion_road_task_detail_btn",
                label = "[任务] 冠军之路按钮",
                distance_anchor_exact_text = "[任务] 冠军之路",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 71.799272,
                distance_max = 76.240464,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 593.211731,
                hint_client_y = 324.490753,
                hint_ratio_x = 0.411953,
                hint_ratio_y = 0.360545,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 708.00,
                fixed_fallback_client_y = 320.00,
                fixed_fallback_ratio_x = 0.491667,
                fixed_fallback_ratio_y = 0.355556,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80,
                retry_ms = 600,
                settle_ms = 1200
            }
        },
        {
            key = "eternal_gilding_champion_road_task_detail_before_jump",
            timeout_ms = 9000,
            origins = {
                "npc",
                "interaction_prompt"
            },
            jump_after_step = true,
            jump_after_step_delay_ms = 200,
            jump_after_step_wait_ms = 1200,
            jump_after_step_window_ms = 5000,
            settle_ms = 1200
        },
        {
            task_patterns = {
                "永恒鎏金",
                "冠军之路"
            },
            task_detail_patterns = {
                "深入铺满纯金的广场",
                "找到神秘人的下落"
            },
            constraint_mode = "all",
            post_dialogue_flow = {
                key = "eternal_gilding_champion_road_wait_after_jump",
                wait_task_info_refresh_after_jump = true,
                task_info_refresh_timeout_ms = 6500
            }
        }
    ),
    ["前往梳洗大厅，阻止神秘人"] = make_world_map_send_task_config(
        "evil_sun_wash_hall_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 659.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.457770,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1200,
            center_click_ratio_x = 0.595139,
            center_click_ratio_y = 0.500000,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "太阳斗场按钮",
                distance_anchor_exact_text = "太阳斗场",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                distance_min = 60.448891,
                distance_max = 64.187998,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 821.359436,
                hint_client_y = 412.743591,
                hint_ratio_x = 0.570388,
                hint_ratio_y = 0.458604,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 857.00,
                fixed_fallback_client_y = 450.00,
                fixed_fallback_ratio_x = 0.595139,
                fixed_fallback_ratio_y = 0.500000,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000
        },
        {
            task_patterns = {
                "邪阳"
            },
            task_detail_patterns = {
                "前往梳洗大厅，阻止神秘人"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            constraint_mode = "all",
            enable_linear_recipe = true
        }
    ),
    ["前往太阳王庭"] = make_world_map_send_task_config(
        "sun_king_court_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 659.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.457770,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1200,
            center_click_ratio_x = 0.722917,
            center_click_ratio_y = 0.498889,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "太阳王庭按钮",
                distance_anchor_exact_text = "太阳王庭",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                distance_min = 143.092981,
                distance_max = 148.092981,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 1006.359741,
                hint_client_y = 412.743591,
                hint_ratio_x = 0.698861,
                hint_ratio_y = 0.458604,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 1041.00,
                fixed_fallback_client_y = 449.00,
                fixed_fallback_ratio_x = 0.722917,
                fixed_fallback_ratio_y = 0.498889,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000,
            defer_revive_during_map_entry = true
        },
        {
            task_patterns = {
                "太阳王庭"
            },
            task_detail_patterns = {
                "前往太阳王庭"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            constraint_mode = "all",
            enable_linear_recipe = true
        }
    ),
    ["锈蚀深渊 / 前往锈蚀深渊"] = make_world_map_send_task_config(
        "rust_depth_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            distance_min = 8.248740,
            distance_max = 9.248740,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            hint_client_x = 659.188843,
            hint_client_y = 827.716736,
            hint_ratio_x = 0.457770,
            hint_ratio_y = 0.919685,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1200,
            center_click_ratio_x = 0.523975,
            center_click_ratio_y = 0.498889,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "锈蚀深渊入口按钮",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 719.359558,
                hint_client_y = 412.743591,
                hint_ratio_x = 0.499555,
                hint_ratio_y = 0.458604,
                hint_max_distance = 80.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 753.00,
                fixed_fallback_client_y = 451.00,
                fixed_fallback_ratio_x = 0.522917,
                fixed_fallback_ratio_y = 0.501111,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000
        },
        {
            task_patterns = {
                "锈蚀深渊",
                "碎骨巨斧"
            },
            task_detail_patterns = {
                "前往锈蚀深渊"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            constraint_mode = "all",
            enable_linear_recipe = true
        }
    ),
    ["主线 叹息之墙 / 前往禁忌高墙"] = make_world_map_send_task_config(
        "wall_of_sighs_forbidden_wall_world_map_send",
        {
            label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{4F20}\u{9001}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
            },
            prefer_hint_fallback = true
        },
        {
            map_open_wait_ms = 1200,
            world_map_panel_missing_fallback_ms = 3500,
            center_click_ratio_x = 0.502778,
            center_click_ratio_y = 0.501111,
            center_use_human_mouse = true,
            center_selection_step = {
                label = "禁忌高墙地图项按钮",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 676.011230,
                hint_client_y = 396.815979,
                hint_ratio_x = 0.469452,
                hint_ratio_y = 0.440907,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 724.00,
                fixed_fallback_client_y = 451.00,
                fixed_fallback_ratio_x = 0.502778,
                fixed_fallback_ratio_y = 0.501111,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80
            },
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
            timeout_ms = 16000
        },
        {
            task_patterns = {
                "叹息之墙"
            },
            task_detail_names = {
                "前往禁忌高墙"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            constraint_mode = "all",
            enable_linear_recipe = true
        }
    ),
    ["击败邪龙"] = make_boss_kite_task_config(
        "rust_depth_defeat_evil_dragon_boss_kite",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            kite_radius = 1800,
            kite_switch_ms = 2000,
            seamless_kite = true,
            kite_arrive_distance = 480,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 2500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 1200,
            nearby_text_task_change_confirm_count = 2,
            kite_points = {
                { x = -1103.25, y = -1297.09, z = 6.00 },
                { x = -2336.17, y = -1241.16, z = 6.00 },
                { x = -2119.15, y = 156.63, z = 6.00 }
            }
        },
        {
            task_patterns = {
                "锈蚀深渊",
                "碎骨巨斧"
            },
            task_detail_patterns = {
                "击败邪龙"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    ),
    ["追击太阳女王，拯救阿瑞娅"] = make_boss_kite_task_config(
        "evil_sun_chase_queen_boss_kite",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            kite_radius = 2200,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 2500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 1200,
            nearby_text_task_change_confirm_count = 2,
            kite_points = {
                { x = -571.96, y = 9730.78, z = 606.00 },
                { x = 172.86, y = 9841.78, z = 606.00 },
                { x = 950.42, y = 9945.04, z = 606.00 },
                { x = 1818.80, y = 10347.91, z = 606.00 }
            }
        },
        {
            task_patterns = {
                "邪阳"
            },
            task_detail_patterns = {
                "追击太阳女王，拯救阿瑞娅"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    ),
    ["击败被腐化的英灵"] = make_boss_kite_task_config(
        "evil_sun_corrupted_hero_boss_kite",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 2500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 1200,
            nearby_text_task_change_confirm_count = 2
        },
        {
            task_patterns = {
                "邪阳"
            },
            task_detail_patterns = {
                "击败被腐化的英灵"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    ),
    ["追踪太阳女王，解救阿瑞娅"] = make_boss_kite_task_config(
        "shadow_sun_chase_queen_boss_kite",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            kite_radius = 2200,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 2500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 1200,
            nearby_text_task_change_confirm_count = 2,
            kite_points = {
                { x = 9138.96, y = 11697.53, z = 2416.00 },
                { x = 8967.00, y = 10986.00, z = 2416.00 },
                { x = 8736.51, y = 11502.49, z = 2416.00 },
                { x = 9100.00, y = 11880.00, z = 2416.00 }
            }
        },
        {
            task_patterns = {
                "恶影拜日"
            },
            task_detail_patterns = {
                "追踪太阳女王，解救阿瑞娅"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    ),
    ["击败虚影之子"] = make_boss_kite_task_config(
        "shadow_sun_defeat_shadow_child_boss_kite",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            kite_radius = 1260,
            kite_point_count = 3,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 2500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 1200,
            nearby_text_task_change_confirm_count = 2
        },
        {
            task_patterns = {
                "恶影拜日"
            },
            task_detail_patterns = {
                "击败虚影之子"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    ),
    ["前往太阳王座，觐见女王"] = make_boss_kite_task_config(
        "audience_road_sun_throne_boss_kite",
        {
            trigger_distance = 1400,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            immediate_no_task_target_kite = true,
            no_task_target_require_near_kite_point = true,
            no_task_target_kite_point_distance = 2600,
            no_task_target_kite_wait_ms = 900,
            kite_radius = 1800,
            kite_switch_ms = 2000,
            seamless_kite = true,
            kite_arrive_distance = 480,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 2500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            ignore_terminal_text_change_when_objective_same = true,
            revive_reentry = make_revive_reentry_config({
                key = "eternal_rust_sun_queen_room_reentry_11591_-15",
                label = "\u{6C38}\u{6052}\u{9508}\u{8680}\u{592A}\u{9633}\u{5973}\u{738B}Boss\u{91CD}\u{8FDB}\u{623F}",
                anchor = {
                    x = 11591.00,
                    y = -15.00,
                    z = 1104.20,
                    radius = 620,
                    z_tolerance = 360
                },
                interact_distance = 300,
                portal_scan_distance = 900,
                retry_ms = 900,
                settle_ms = 1400,
                timeout_ms = 22000,
                post_transition_boss_engage_ms = 16000,
                task_patterns = {
                    "永恒锈蚀"
                },
                task_detail_patterns = {
                    "击败太阳女王"
                },
                constraint_mode = "all",
                fallback_interact = true
            }),
            kite_points = {
                { x = 14140.00, y = 820.00, z = 1215.00 },
                { x = 13125.48, y = 743.95, z = 1215.00 },
                { x = 12998.45, y = -473.70, z = 1215.00 },
                { x = 14082.77, y = -669.93, z = 1215.00 }
            }
        },
        {
            task_patterns = {
                "谒见之路",
                "永恒锈蚀"
            },
            task_detail_patterns = {
                "前往太阳王座，觐见女王",
                "击败太阳女王"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    ),
    ["询问打开大门的方法"] = make_dialogue_locator_flow_task_config(
        "champion_road_task_detail_dialogue_flow",
        {
            {
                key = "champion_road_task_detail_btn",
                label = "[任务] 冠军之路按钮",
                distance_anchor_exact_text = "[任务] 冠军之路",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 71.799272,
                distance_max = 76.240464,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 593.211731,
                hint_client_y = 324.490753,
                hint_ratio_x = 0.411953,
                hint_ratio_y = 0.360545,
                hint_max_distance = 30.000,
                prefer_hint_fallback = true,
                poll_retry_count = 30,
                poll_interval_ms = 100,
                fixed_fallback_client_x = 708.00,
                fixed_fallback_client_y = 320.00,
                fixed_fallback_ratio_x = 0.491667,
                fixed_fallback_ratio_y = 0.355556,
                fixed_fallback_prefer_ratio = true,
                fixed_fallback_mouse_mode = "api",
                fixed_fallback_click_delay_ms = 50,
                fixed_fallback_hover_delay_ms = 80,
                retry_ms = 600,
                settle_ms = 1200
            }
        },
        {
            key = "champion_road_task_detail_dialogue_flow",
            timeout_ms = 9000,
            origins = {
                "npc",
                "interaction_prompt"
            },
            jump_after_step = true,
            jump_after_step_delay_ms = 200,
            jump_after_step_wait_ms = 1200,
            jump_after_step_window_ms = 5000,
            settle_ms = 1200
        },
        {
            task_patterns = {
                "冠军之路"
            },
            task_detail_patterns = {
                "询问打开大门的方法"
            },
            constraint_mode = "all"
        }
    ),
    ["击败上弦之阿兹尔"] = make_boss_kite_task_config(
        "double_strings_upper_azil_boss",
        {
            trigger_distance = 700,
            immediate_kite_on_reached = true,
            kite_radius = 1800,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 1200,
            nearby_text_task_change_confirm_count = 2
        },
        {
            task_patterns = {
                "双弦"
            },
            task_detail_patterns = {
                "击败上弦之阿兹尔"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all",
            main_task_call = {
                allow_anchor_click_fallback = true
            }
        }
    ),
    ["双弦 / 击败下弦之默吉特"] = make_boss_kite_task_config(
        "double_strings_second_boss",
        {
            trigger_distance = 700,
            immediate_kite_on_reached = true,
            kite_radius = 1800,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 120,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 1200,
            nearby_text_task_change_confirm_count = 2
        },
        {
            task_patterns = {
                "双弦"
            },
            task_detail_patterns = {
                "击败下弦之默吉特",
                "击败下弦"
            },
            constraint_mode = "all",
            main_task_call = {
                allow_anchor_click_fallback = true
            }
        }
    ),
    ["通过三处试炼，追击夺走火种的神秘人"] = make_dialogue_locator_flow_task_config(
        "trial_of_sun_choose_trial_reverse_dialogue_flow",
        {
            {
                key = "trial_of_sun_choose_next_trial_power_first",
                label = "太阳的试炼_选择试炼_权力优先",
                retry_ms = 600,
                settle_ms = 1200,
                locator_candidate_sequence = true,
                locator_candidate_sequence_key = "trial_of_sun_trials_power_first",
                locator_candidate_sequence_share_across_details = true,
                locator_candidates = {
                    {
                        key = "trial_of_sun_choose_power_trial",
                        label = "权力试炼按钮",
                        distance_anchor_exact_text = "权力试炼",
                        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                        distance_min = 61.059764,
                        distance_max = 64.836656,
                        include_patterns = {
                            "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                        },
                        hint_client_x = 622.029968,
                        hint_client_y = 356.017120,
                        hint_ratio_x = 0.431965,
                        hint_ratio_y = 0.395575,
                        hint_max_distance = 80.000,
                        retry_ms = 600,
                        settle_ms = 1200
                    },
                    {
                        key = "trial_of_sun_choose_conquest_trial",
                        label = "征伐试炼按钮",
                        distance_anchor_exact_text = "征伐试炼",
                        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                        distance_min = 61.059758,
                        distance_max = 64.836650,
                        include_patterns = {
                            "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                        },
                        hint_client_x = 622.029968,
                        hint_client_y = 300.386353,
                        hint_ratio_x = 0.431965,
                        hint_ratio_y = 0.333763,
                        hint_max_distance = 80.000,
                        retry_ms = 600,
                        settle_ms = 1200
                    },
                    {
                        key = "trial_of_sun_choose_beauty_trial",
                        label = "美欲试炼按钮",
                        distance_anchor_exact_text = "美欲试炼",
                        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                        distance_min = 61.059764,
                        distance_max = 64.836656,
                        include_patterns = {
                            "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                        },
                        hint_client_x = 622.029968,
                        hint_client_y = 411.647919,
                        hint_ratio_x = 0.431965,
                        hint_ratio_y = 0.457387,
                        hint_max_distance = 80.000,
                        retry_ms = 600,
                        settle_ms = 1200
                    }
                }
            }
        },
        {
            key = "trial_of_sun_choose_trial_reverse_dialogue_flow",
            timeout_ms = 9000,
            timeout_close_dialogue_and_refresh = true,
            timeout_refresh_wait_ms = 1200,
            jump_after_step = true,
            jump_after_step_delay_ms = 200,
            jump_after_step_wait_ms = 1200,
            jump_after_step_window_ms = 5000,
            origins = {
                "npc",
                "interaction_prompt"
            },
            settle_ms = 1200
        },
        {
            task_patterns = TRIAL_OF_SUN_MAIN_TASK_PATTERNS,
            task_detail_patterns = TRIAL_OF_SUN_MAIN_DETAIL_PATTERNS,
            constraint_mode = "all",
            post_dialogue_flow = {
                key = "trial_of_sun_repeat_npc_dialogue_until_three_trials",
                mode = "after_jump_route_action",
                steps = {},
                initial_delay_ms = 900,
                timeout_ms = 9000,
                followup_route_action_key = "trial_of_sun_prophecy_site_dialogue_-342_1891",
                followup_route_action_source = "trial_of_sun_dialogue_chain",
                followup_route_action_ignore_retry = true,
                followup_locator_candidate_sequence_key = "trial_of_sun_trials_power_first",
                followup_locator_candidate_count = 3,
                followup_locator_candidate_sequence_share_across_details = true
            },
            defer_blocking_side_tasks_until_dialogue_chain = {
                key = "trial_of_sun_accept_all_trials_before_side_routes",
                route_action_key = "trial_of_sun_prophecy_site_dialogue_-342_1891",
                route_action_source = "trial_of_sun_blocking_side_task_gate",
                ignore_route_action_retry = true,
                locator_candidate_sequence_key = "trial_of_sun_trials_power_first",
                locator_candidate_count = 3,
                locator_candidate_sequence_share_across_details = true,
                trigger = {
                    x = -341.84,
                    y = 1891.37,
                    z = 1235.00,
                    radius = 2200,
                    z_tolerance = 320
                }
            },
            recipe_package = {
                key = TRIAL_OF_SUN_THREE_TRIALS_PACKAGE_KEY,
                label = "太阳的试炼_三试炼任务包",
                side_task_order = {
                    TRIAL_OF_SUN_POWER_SIDE_KEY,
                    TRIAL_OF_SUN_CONQUEST_SIDE_KEY,
                    TRIAL_OF_SUN_BEAUTY_SIDE_KEY
                }
            },
            blocking_side_tasks = {
                {
                    key = TRIAL_OF_SUN_POWER_SIDE_KEY,
                    recipe_package_key = TRIAL_OF_SUN_THREE_TRIALS_PACKAGE_KEY,
                    recipe_side_task_key = TRIAL_OF_SUN_POWER_SIDE_KEY,
                    task_name = "支线 权力之试",
                    task_detail = "通过权力试炼，获得权力之瞳的力量",
                    queries = {
                        "权力之试",
                        "通过权力试炼",
                        "权力之瞳"
                    },
                    task_patterns = {
                        "权力之试"
                    },
                    task_detail_patterns = {
                        "通过权力试炼",
                        "权力之瞳"
                    }
                },
                {
                    key = TRIAL_OF_SUN_CONQUEST_SIDE_KEY,
                    recipe_package_key = TRIAL_OF_SUN_THREE_TRIALS_PACKAGE_KEY,
                    recipe_side_task_key = TRIAL_OF_SUN_CONQUEST_SIDE_KEY,
                    task_name = "支线 征伐之试",
                    task_detail = "通过征伐试炼，获得征伐之瞳的力量",
                    queries = {
                        "征伐之试",
                        "通过征伐试炼",
                        "征伐之瞳"
                    },
                    task_patterns = {
                        "征伐之试"
                    },
                    task_detail_patterns = {
                        "通过征伐试炼",
                        "征伐之瞳"
                    }
                },
                {
                    key = TRIAL_OF_SUN_BEAUTY_SIDE_KEY,
                    recipe_package_key = TRIAL_OF_SUN_THREE_TRIALS_PACKAGE_KEY,
                    recipe_side_task_key = TRIAL_OF_SUN_BEAUTY_SIDE_KEY,
                    task_name = "支线 美欲之试",
                    task_detail = "通过美欲试炼，获得美欲之瞳的力量",
                    queries = {
                        "美欲之试",
                        "通过美欲试炼",
                        "美欲之瞳"
                    },
                    task_patterns = {
                        "美欲之试"
                    },
                    task_detail_patterns = {
                        "通过美欲试炼",
                        "美欲之瞳"
                    }
                }
            }
        }
    ),
    ["支线 美欲之试"] = {
        task_patterns = {
            "美欲之试"
        },
        task_detail_patterns = {
            "通过美欲试炼",
            "美欲之瞳"
        },
        recipe_package_key = TRIAL_OF_SUN_THREE_TRIALS_PACKAGE_KEY,
        recipe_side_task_key = TRIAL_OF_SUN_BEAUTY_SIDE_KEY,
        allow_non_mainline_task_button = true
    },
    ["支线 权力之试"] = {
        task_patterns = {
            "权力之试"
        },
        task_detail_patterns = {
            "通过权力试炼",
            "权力之瞳"
        },
        recipe_package_key = TRIAL_OF_SUN_THREE_TRIALS_PACKAGE_KEY,
        recipe_side_task_key = TRIAL_OF_SUN_POWER_SIDE_KEY,
        allow_non_mainline_task_button = true
    },
    ["支线 征伐之试"] = {
        task_patterns = {
            "征伐之试"
        },
        task_detail_patterns = {
            "通过征伐试炼",
            "征伐之瞳"
        },
        recipe_package_key = TRIAL_OF_SUN_THREE_TRIALS_PACKAGE_KEY,
        recipe_side_task_key = TRIAL_OF_SUN_CONQUEST_SIDE_KEY,
        allow_non_mainline_task_button = true
    },
}

M.TASK_NAME_CONFIGS["美欲之试"] = M.TASK_NAME_CONFIGS["支线 美欲之试"]
M.TASK_NAME_CONFIGS["权力之试"] = M.TASK_NAME_CONFIGS["支线 权力之试"]
M.TASK_NAME_CONFIGS["征伐之试"] = M.TASK_NAME_CONFIGS["支线 征伐之试"]

M.TASK_NAME_CONFIGS["\u{5723}\u{8BEB}\u{4E4B}\u{672B}"] = make_world_map_send_task_config(
    "ember_rest_world_map_send",
    {
        label = "\u{4F20}\u{9001}\u{6309}\u{94AE}",
        distance_anchor_exact_text = "\u{4F20}\u{9001}",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn",
        distance_min = 8.248740,
        distance_max = 9.248740,
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.WorldMapDetail_C.WidgetTree.WorldMapDetailItem.WidgetTree.SendBtn"
        },
        hint_client_x = 659.188843,
        hint_client_y = 827.716736,
        hint_ratio_x = 0.457770,
        hint_ratio_y = 0.919685,
        hint_max_distance = 80.000,
        prefer_hint_fallback = true
    },
    {
        map_open_wait_ms = 1900,
        center_click_ratio_x = 0.503822,
        center_click_ratio_y = 0.495556,
        center_use_human_mouse = true,
        center_selection_step = {
            label = "\u{907F}\u{96BE}\u{6240}-\u{4F59}\u{70EC}\u{4E4B}\u{606F}\u{6309}\u{94AE}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
            },
            hint_client_x = 676.011230,
            hint_client_y = 395.815979,
            hint_ratio_x = 0.469452,
            hint_ratio_y = 0.439796,
            hint_max_distance = 30.000,
            prefer_hint_fallback = true,
            poll_retry_count = 30,
            poll_interval_ms = 100,
            fixed_fallback_client_x = 725.00,
            fixed_fallback_client_y = 444.00,
            fixed_fallback_ratio_x = 0.503472,
            fixed_fallback_ratio_y = 0.493333,
            fixed_fallback_prefer_ratio = true,
            fixed_fallback_mouse_mode = "api",
            fixed_fallback_click_delay_ms = 50,
            fixed_fallback_hover_delay_ms = 80
        },
        center_mouse_mode = "api",
        center_hover_delay_ms = 90,
        center_click_delay_ms = 60,
        center_settle_ms = 750,
        center_retry_ms = 1400,
        transition_wait_ms = 2500,
        timeout_ms = 16000
    },
    {
        task_names = {
            "\u{5723}\u{8BEB}\u{4E4B}\u{672B}",
            "\u{4E3B}\u{7EBF} \u{5723}\u{8BEB}\u{4E4B}\u{672B}"
        },
        task_patterns = {
            "\u{5723}\u{8BEB}\u{4E4B}\u{672B}"
        },
        task_detail_names = {
            "\u{524D}\u{5F80}\u{4F59}\u{70EC}\u{4E4B}\u{606F}"
        },
        task_detail_patterns = {
            "\u{524D}\u{5F80}\u{4F59}\u{70EC}\u{4E4B}\u{606F}"
        },
        constraint_mode = "all",
        main_task_call = {
            allow_anchor_click_fallback = true
        },
        enable_linear_recipe = true
    }
)
M.TASK_NAME_CONFIGS["\u{4E3B}\u{7EBF} \u{5723}\u{8BEB}\u{4E4B}\u{672B}"] = M.TASK_NAME_CONFIGS["\u{5723}\u{8BEB}\u{4E4B}\u{672B}"]

M.GUIDE_SKIP_STEP = {
    label = "新手引导跳过按钮",
    escape_first = false,
    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.NoviceGuideMainUI_C.WidgetTree.C_SkipButton",
    include_patterns = {
        "UIButton Transient.GameEngine.CoreGameInstance.NoviceGuideMainUI_C.WidgetTree.C_SkipButton"
    },
    hint_client_x = 758.667236,
    hint_client_y = 124.673813,
    hint_ratio_x = 0.526852,
    hint_ratio_y = 0.138526,
    hint_max_distance = 40.000
}

M.GLOBAL_TASK_PORTAL_STEP = {
    label = "鐮寸閾佹枾鎸夐挳",
    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
    include_patterns = {
        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
    },
    hint_client_x = 697.204834,
    hint_client_y = 724.439941,
    hint_ratio_x = 0.484170,
    hint_ratio_y = 0.804933,
    hint_max_distance = 180.000
}

M.COMMON_REVIVE_REENTRY_PORTALS = {
    {
        key = "ancient_battlefield_reentry_portal_10318_-7239",
        label = "上古战场复活进门",
        x = 10318.68,
        y = -7239.43,
        z = 566.00,
        path_match_radius = 900,
        portal_match_radius = 1800,
        player_radius = 1600,
        button_probe_radius = 700,
        portal_button_poll_count = 4,
        portal_button_poll_interval_ms = 120,
        z_tolerance = 260,
        require_enum_portal = true,
        retry_ms = 1200,
        settle_ms = 1200,
        timeout_ms = 45000,
        task_pos_reject_extra_ms = 3500
    },
    {
        key = "mountain_heart_dwarf_king_reentry_portal_4529_13974",
        label = "群山之心矮人王复活进门",
        x = 4526.00,
        y = 13974.00,
        z = 57.44,
        path_match_radius = 900,
        portal_match_radius = 1800,
        player_radius = 1400,
        button_probe_radius = 700,
        portal_button_poll_count = 4,
        portal_button_poll_interval_ms = 120,
        z_tolerance = 320,
        require_enum_portal = true,
        retry_ms = 1200,
        settle_ms = 1200,
        timeout_ms = 45000,
        task_pos_reject_extra_ms = 3500
    }
}

M.ROUTE_POINT_ACTIONS = {
    make_route_point_action({
        key = "ancient_battlefield_rescue_barrel_dwarf_action_5138_2465",
        label = "上古战场_帮被卡在木桶中的矮人出来_无坐标固定点交互",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "上古战场"
        },
        task_detail_patterns = {
            "帮被卡在木桶中的矮人出来"
        },
        constraint_mode = "all",
        trigger = {
            x = 5138.30,
            y = 2464.59,
            z = 566.00,
            radius = 1800,
            z_tolerance = 260
        },
        objective_point = {
            x = 5138.30,
            y = 2464.59,
            z = 566.00,
            radius = 160,
            z_tolerance = 260
        },
        interact_radius = 160,
        probe_retry_ms = 700,
        retry_ms = 600000,
        settle_ms = 2400,
        timeout_ms = 18000,
        hotkey = "D",
        hotkey_label = "上古战场木桶矮人交互",
        hotkey_repeat_count = 1,
        force_task_call_after_transition = false
    }),
    make_npc_dialogue_route_action({
        key = "dragonfall_wilds_ask_keli_dialogue_10234_26476",
        label = "龙陨之野_向科里询问情报_NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        drop_active_when_task_mismatch = true,
        retry_ms = 600000,
        task_patterns = {
            "龙陨之野"
        },
        task_detail_patterns = {
            "向科里询问情报"
        },
        constraint_mode = "all",
        trigger = {
            x = 10233.67,
            y = 26475.80,
            z = 5235.18,
            radius = 1800,
            z_tolerance = 520
        },
        dialogue = {
            x = 10233.67,
            y = 26475.80,
            z = 5235.18,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 220,
            z_tolerance = 520,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 900,
            fallback_interact = true
        }
    }),
    make_sun_faction_choice_action(),
    make_sun_faction_no_path_recover_route_action(),
    make_sun_faction_after_join_route_action(),
    make_daylight_rivalry_arena_hero_route_action(),
    make_daylight_rivalry_baptism_anchor_route_action(),
    make_daylight_rivalry_audience_queen_no_path_route_action(),
    make_daylight_rivalry_audience_queen_dialogue_route_action(),
    make_daylight_rivalry_talk_to_ariya_route_action(),
    make_evil_sun_chase_queen_detour_route_action(),
    make_shadow_sun_chase_queen_detour_route_action(),
    make_route_point_action({
        key = "day_of_apotheosis_enter_ascension_hall_move_to_portal_1692_25481",
        label = "\u{6210}\u{795E}\u{4E4B}\u{65E5}_\u{8FDB}\u{5165}\u{5347}\u{534E}\u{79D8}\u{6BBF}_\u{79FB}\u{52A8}\u{5230}\u{4F20}\u{9001}\u{95E8}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "day_of_apotheosis_enter_ascension_hall_portal_1692_25481",
        task_patterns = {
            "\u{6210}\u{795E}\u{4E4B}\u{65E5}"
        },
        task_detail_patterns = {
            "\u{8FDB}\u{5165}\u{5347}\u{534E}\u{79D8}\u{6BBF}\u{FF0C}\u{963B}\u{6B62}\u{57FA}\u{5188}"
        },
        constraint_mode = "all",
        trigger = {
            x = 1691.62,
            y = 25481.29,
            z = 503.00,
            radius = 1800,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        waypoints = {
            { x = 1691.62, y = 25481.29, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "day_of_apotheosis_enter_ascension_hall_portal_1692_25481",
        label = "\u{6210}\u{795E}\u{4E4B}\u{65E5}_\u{8FDB}\u{5165}\u{5347}\u{534E}\u{79D8}\u{6BBF}_PortalBtn",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "\u{6210}\u{795E}\u{4E4B}\u{65E5}"
        },
        task_detail_patterns = {
            "\u{8FDB}\u{5165}\u{5347}\u{534E}\u{79D8}\u{6BBF}\u{FF0C}\u{963B}\u{6B62}\u{57FA}\u{5188}"
        },
        constraint_mode = "all",
        trigger = {
            x = 1691.62,
            y = 25481.29,
            z = 503.00,
            radius = 720,
            z_tolerance = 360
        },
        interact_radius = 240,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 4500,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        fallback_interact = true,
        fallback_interact_distance = 280,
        fallback_retry_ms = 2500,
        step = {
            key = "day_of_apotheosis_enter_ascension_hall_portal_btn_1692_25481",
            label = "\u{5347}\u{534E}\u{79D8}\u{6BBF}\u{4F20}\u{9001}\u{95E8}PortalBtn",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
            },
            hint_client_x = 697.204834,
            hint_client_y = 724.439941,
            hint_ratio_x = 0.484170,
            hint_ratio_y = 0.804933,
            hint_max_distance = 180.000
        }
    }),
    make_route_point_action({
        key = "holy_fire_academy_clue_detour_5626_15854",
        label = "holy_fire_academy_clue_detour_5626_15854",
        mode = "recorded_route_point",
        allow_wait_task_path_recover = true,
        task_patterns = {
            "\u{5723}\u{6D01}\u{4E4B}\u{706B}"
        },
        task_detail_patterns = {
            "\u{6DF1}\u{5165}\u{5B66}\u{57CE}\u{FF0C}\u{5BFB}\u{627E}\u{7EBF}\u{7D22}"
        },
        constraint_mode = "all",
        trigger = {
            x = 5625.65,
            y = 15853.94,
            z = 519.00,
            radius = 700,
            z_tolerance = 220
        },
        retry_ms = 600000,
        timeout_ms = 45000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 6538.37, y = 15782.33, z = 519.00 },
            { x = 7564.20, y = 15750.88, z = 519.00 },
            { x = 9006.98, y = 16825.09, z = 526.47 }
        }
    }),
    make_route_point_action({
        key = "holy_fire_enter_academy_deep_wait_path_recover_14450_15820",
        label = "圣洁之火_进入学城深处_等待路径恢复",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        wait_task_path_recover_only = true,
        task_patterns = {
            "\u{5723}\u{6D01}\u{4E4B}\u{706B}"
        },
        task_detail_patterns = {
            "\u{8FDB}\u{5165}\u{5B66}\u{57CE}\u{6DF1}\u{5904}"
        },
        constraint_mode = "all",
        trigger = {
            x = 14450.00,
            y = 15820.00,
            z = 1714.00,
            radius = 800,
            z_tolerance = 420
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 15087.28, y = 15779.39, z = 1714.00 },
            { x = 16204.95, y = 15697.74, z = 1714.00 }
        }
    }),
    make_npc_dialogue_route_action({
        key = "holy_fire_ask_madlan_dialogue_14795_15865",
        label = "圣洁之火_向马德兰询问情报_NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        retry_ms = 600000,
        task_patterns = {
            "圣洁之火"
        },
        task_detail_patterns = {
            "向马德兰询问情报"
        },
        constraint_mode = "all",
        trigger = {
            x = 14794.81,
            y = 15865.00,
            z = 1714.00,
            radius = 2200,
            z_tolerance = 420
        },
        dialogue = {
            x = 14794.81,
            y = 15865.00,
            z = 1714.00,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 220,
            z_tolerance = 420,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "shadowland_fire_seed_route_8894_8983",
        label = "燃烧的长夜_火种拾取_固定点路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "shadowland_fire_seed_pickup_a_8894_8983",
        task_patterns = {
            "燃烧的长夜"
        },
        task_detail_patterns = {
            "取回来之不易的火种",
            "寒光巨斧"
        },
        constraint_mode = "all",
        trigger = {
            x = 8894.00,
            y = 8983.00,
            z = 1231.00,
            radius = 1600,
            z_tolerance = 420
        },
        retry_ms = 600000,
        timeout_ms = 18000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        waypoints = {
            { x = 8894.00, y = 8983.00, z = 1231.00 }
        }
    }),
    make_route_point_action({
        key = "shadowland_fire_seed_pickup_a_8894_8983",
        label = "燃烧的长夜_火种拾取_A键两次",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "燃烧的长夜"
        },
        constraint_mode = "all",
        trigger = {
            x = 8894.00,
            y = 8983.00,
            z = 1231.00,
            radius = 620,
            z_tolerance = 420
        },
        interact_radius = 240,
        pre_action_combat_guard = false,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 1600,
        timeout_ms = 14000,
        hotkey = "A",
        hotkey_repeat_count = 2,
        hotkey_interval_ms = 180,
        hotkey_label = "fire seed pickup",
        followup_route_action_key = "shadowland_fire_seed_route_10068_9823"
    }),
    make_route_point_action({
        key = "shadowland_fire_seed_route_10068_9823",
        label = "燃烧的长夜_火种拾取后_移动到Gather点",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "shadowland_fire_seed_gather_10068_9823",
        task_patterns = {
            "燃烧的长夜"
        },
        task_detail_patterns = {
            "取回来之不易的火种",
            "寒光巨斧"
        },
        constraint_mode = "all",
        trigger = {
            x = 8894.00,
            y = 8983.00,
            z = 1231.00,
            radius = 760,
            z_tolerance = 420
        },
        retry_ms = 600000,
        timeout_ms = 18000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        waypoints = {
            { x = 10068.00, y = 9823.00, z = 1231.00 }
        }
    }),
    make_route_point_action({
        key = "shadowland_fire_seed_gather_10068_9823",
        label = "燃烧的长夜_火种拾取后_Gather",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "燃烧的长夜"
        },
        task_detail_patterns = {
            "取回来之不易的火种",
            "寒光巨斧"
        },
        constraint_mode = "all",
        trigger = {
            x = 10068.00,
            y = 9823.00,
            z = 1231.00,
            radius = 520,
            z_tolerance = 420
        },
        interact_radius = 260,
        pre_action_combat_guard = false,
        probe_retry_ms = 700,
        retry_ms = 600000,
        settle_ms = 1600,
        timeout_ms = 14000,
        missing_button_complete_action = true,
        missing_button_skip_after_ms = 3000,
        step = {
            key = "shadowland_fire_seed_gather_button_10068_9823",
            label = "火种后续Gather按钮",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn"
            },
            hint_client_x = 691.053040,
            hint_client_y = 701.797729,
            hint_ratio_x = 0.480231,
            hint_ratio_y = 0.779775,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true,
            settle_ms = 1200
        }
    }),
    make_route_point_action({
        key = "audience_road_sun_throne_detour_-4054_8509",
        label = "谒见之路_前往太阳王座_补充录制路线",
        mode = "recorded_route_point",
        allow_wait_task_path_recover = true,
        task_patterns = {
            "谒见之路"
        },
        task_detail_patterns = {
            "前往太阳王座，觐见女王"
        },
        constraint_mode = "all",
        trigger = {
            x = -4054.00,
            y = 8509.00,
            z = 5.00,
            radius = 900,
            z_tolerance = 220
        },
        retry_ms = 600000,
        timeout_ms = 45000,
        waypoint_reach_radius = 240,
        waypoint_z_tolerance = 220,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -3638.76, y = 7955.01, z = 5.00 },
            { x = -2787.62, y = 7533.55, z = 5.00 },
            { x = -2813.32, y = 6432.35, z = 5.00 }
        }
    }),
    make_npc_dialogue_route_action({
        key = "trial_of_sun_prophecy_site_dialogue_-342_1891",
        label = "太阳的试炼_通过三处试炼_NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = TRIAL_OF_SUN_MAIN_TASK_PATTERNS,
        task_detail_patterns = TRIAL_OF_SUN_MAIN_DETAIL_PATTERNS,
        constraint_mode = "all",
        trigger = {
            x = -341.84,
            y = 1891.37,
            z = 1235.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 6000,
        dialogue = {
            x = -341.84,
            y = 1891.37,
            z = 1235.00,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 180,
            z_tolerance = 220,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    }),
    make_trial_of_sun_recorded_route_action(TRIAL_OF_SUN_BEAUTY_SIDE_KEY, {
        key = "trial_of_sun_beauty_side_route_-903_1940",
        label = "太阳的试炼_美欲之试_起步录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        task_patterns = {
            "美欲之试",
            "通过美欲试炼"
        },
        task_detail_patterns = {
            "通过美欲试炼",
            "美欲之瞳"
        },
        trigger = {
            x = -902.53,
            y = 1940.46,
            z = 1235.00,
            radius = 1800,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 170000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "trial_of_sun_beauty_maptrap_01",
        waypoints = {
            { x = -902.53, y = 1940.46, z = 1235.00 },
            { x = 316.95, y = 2014.15, z = 1235.00 },
            { x = 1347.97, y = 2225.41, z = 1235.00 },
            { x = 2084.44, y = 2274.41, z = 1235.00 },
            { x = 2964.65, y = 2122.50, z = 1235.00 },
            { x = 3703.95, y = 1767.95, z = 1235.00 },
            { x = 4760.88, y = 1045.31, z = 1235.00 },
            { x = 5628.44, y = 1024.55, z = 1235.00 },
            { x = 6369.17, y = 991.11, z = 1235.00 },
            { x = 6981.69, y = 652.08, z = 1235.00 },
            { x = 7586.13, y = 155.47, z = 1235.00 },
            { x = 8178.73, y = -85.65, z = 1235.00 },
            { x = 8932.08, y = -112.39, z = 1235.00 },
            { x = 9829.01, y = -130.31, z = 1235.00 },
            { x = 10474.68, y = -323.79, z = 1235.00 },
            { x = 10841.03, y = -836.47, z = 1235.00 },
            { x = 11027.62, y = -1426.70, z = 1235.00 },
            { x = 11250.67, y = -2242.51, z = 1235.00 },
            { x = 11713.94, y = -2712.61, z = 1235.00 },
            { x = 12340.05, y = -3000.22, z = 1235.00 },
            { x = 13275.31, y = -3796.30, z = 1235.00 },
            { x = 13637.27, y = -4205.53, z = 1235.00 },
            { x = 14361.08, y = -4019.12, z = 1235.00 },
            { x = 14945.02, y = -3861.60, z = 1235.00 },
            { x = 15493.23, y = -4096.46, z = 1235.00 },
            { x = 16124.06, y = -4269.22, z = 1235.00 },
            { x = 16708.75, y = -4441.26, z = 1235.00 },
            { x = 17247.16, y = -4723.81, z = 1235.00 },
            { x = 17432.13, y = -5245.92, z = 1235.00 },
            { x = 17439.93, y = -6074.04, z = 1235.00 }
        }
    }),
    make_trial_of_sun_side_maptrap_action({
        prefix = "trial_of_sun_beauty",
        label = "太阳的试炼_美欲之试",
        button_label = "美欲之试",
        index = 1,
        task_patterns = {
            "美欲之试",
            "通过美欲试炼"
        },
        task_detail_patterns = {
            "通过美欲试炼",
            "美欲之瞳"
        },
        from_point = { x = 17439.93, y = -6074.04, z = 1235.00, radius = 900, z_tolerance = 260 },
        objective_point = { x = 17439.93, y = -6074.04, z = 1235.00 },
        followup_route_action_key = "trial_of_sun_beauty_return_route_17440_-6074"
    }),
    make_trial_of_sun_recorded_route_action(TRIAL_OF_SUN_BEAUTY_SIDE_KEY, {
        key = "trial_of_sun_beauty_return_route_17440_-6074",
        label = "太阳的试炼_美欲之试_原路返回",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "美欲之试",
            "通过美欲试炼",
            "太阳的试炼",
            "通过三处试炼"
        },
        task_detail_patterns = {
            "通过美欲试炼",
            "美欲之瞳",
            "通过三处试炼",
            "追击夺走火种"
        },
        trigger = {
            x = 17439.93,
            y = -6074.04,
            z = 1235.00,
            radius = 1200,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "trial_of_sun_beauty_maptrap_02",
        waypoints = {
            { x = 17432.13, y = -5245.92, z = 1235.00 },
            { x = 17247.16, y = -4723.81, z = 1235.00 },
            { x = 16708.75, y = -4441.26, z = 1235.00 },
            { x = 16124.06, y = -4269.22, z = 1235.00 },
            { x = 15493.23, y = -4096.46, z = 1235.00 },
            { x = 14945.02, y = -3861.60, z = 1235.00 },
            { x = 14361.08, y = -4019.12, z = 1235.00 },
            { x = 13637.27, y = -4205.53, z = 1235.00 },
            { x = 13275.31, y = -3796.30, z = 1235.00 },
            { x = 12340.05, y = -3000.22, z = 1235.00 },
            { x = 11713.94, y = -2712.61, z = 1235.00 },
            { x = 11250.67, y = -2242.51, z = 1235.00 },
            { x = 11027.62, y = -1426.70, z = 1235.00 },
            { x = 10841.03, y = -836.47, z = 1235.00 },
            { x = 10474.68, y = -323.79, z = 1235.00 },
            { x = 9829.01, y = -130.31, z = 1235.00 },
            { x = 8932.08, y = -112.39, z = 1235.00 },
            { x = 8178.73, y = -85.65, z = 1235.00 },
            { x = 7586.13, y = 155.47, z = 1235.00 },
            { x = 6981.69, y = 652.08, z = 1235.00 },
            { x = 6369.17, y = 991.11, z = 1235.00 },
            { x = 5628.44, y = 1024.55, z = 1235.00 },
            { x = 4760.88, y = 1045.31, z = 1235.00 },
            { x = 3703.95, y = 1767.95, z = 1235.00 },
            { x = 2964.65, y = 2122.50, z = 1235.00 },
            { x = 2084.44, y = 2274.41, z = 1235.00 },
            { x = 1347.97, y = 2225.41, z = 1235.00 },
            { x = 316.95, y = 2014.15, z = 1235.00 },
            { x = -902.53, y = 1940.46, z = 1235.00 },
            { x = 1321.54, y = 3029.03, z = 1235.00 }
        }
    }),
    make_trial_of_sun_side_maptrap_action({
        prefix = "trial_of_sun_beauty",
        label = "太阳的试炼_美欲之试",
        button_label = "美欲之试",
        index = 2,
        task_patterns = {
            "美欲之试",
            "通过美欲试炼",
            "太阳的试炼",
            "通过三处试炼"
        },
        task_detail_patterns = {
            "通过美欲试炼",
            "美欲之瞳",
            "通过三处试炼",
            "追击夺走火种"
        },
        from_point = { x = 1321.54, y = 3029.03, z = 1235.00, radius = 900, z_tolerance = 260 },
        objective_point = { x = 1321.54, y = 3029.03, z = 1235.00 }
    }),
    make_route_point_action({
        key = "trial_of_sun_beauty_finish_function_0_4050",
        label = "太阳的试炼_美欲之试_终点交互",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "美欲之试",
            "通过美欲试炼",
            "太阳的试炼",
            "通过三处试炼"
        },
        task_detail_patterns = {
            "通过美欲试炼",
            "美欲之瞳",
            "通过三处试炼",
            "追击夺走火种",
            "完成"
        },
        constraint_mode = "all",
        trigger = {
            x = 336.00,
            y = 3808.00,
            z = 1230.00,
            radius = 900,
            z_tolerance = 320
        },
        objective_point = {
            x = 336.00,
            y = 3808.00,
            z = 1230.00,
            radius = 900,
            z_tolerance = 320
        },
        interact_radius = 900,
        pre_action_combat_guard = false,
        probe_retry_ms = 650,
        retry_ms = 600000,
        settle_ms = 1800,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "trial_of_sun_beauty_finish_function_btn_0_4050",
            label = "美欲试炼终点过图按钮",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.LiftBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.LiftBtn"
            },
            hint_client_x = 693.528564,
            hint_client_y = 695.631531,
            hint_ratio_x = 0.481617,
            hint_ratio_y = 0.772924,
            hint_max_distance = 100.000,
            prefer_hint_fallback = true,
            settle_ms = 1200,
            task_pos_reject_extra_ms = 3500
        }
    }),
    make_trial_of_sun_recorded_route_action(TRIAL_OF_SUN_POWER_SIDE_KEY, {
        key = "trial_of_sun_power_side_route_-433_1937",
        label = "太阳的试炼_权力之试_起步录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        task_patterns = {
            "权力之试",
            "通过权力试炼"
        },
        task_detail_patterns = {
            "通过权力试炼",
            "权力之瞳"
        },
        trigger = {
            x = -433.00,
            y = 1937.00,
            z = 1235.00,
            radius = 1800,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 130000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        waypoints = {
            { x = -433.00, y = 1937.00, z = 1235.00 },
            { x = -1520.58, y = 2845.21, z = 1235.00 },
            { x = -2165.18, y = 4186.24, z = 1235.00 },
            { x = -1201.09, y = 6022.99, z = 1235.00 },
            { x = -64.26, y = 7478.86, z = 1235.00 },
            { x = 55.24, y = 9854.43, z = 1235.00 },
            { x = 597.48, y = 10920.42, z = 1235.00 },
            { x = 74.86, y = 12022.68, z = 1235.00 },
            { x = -710.00, y = 13043.00, z = 1235.00 },
            { x = -719.94, y = 13786.32, z = 1235.00 },
            { x = -124.97, y = 14627.39, z = 1235.00 },
            { x = 300.97, y = 16185.74, z = 1235.00 }
        }
    }),
    make_trial_of_sun_power_maptrap_action(1,
        { x = 300.97, y = 16185.74, z = 1235.00, radius = 1600, z_tolerance = 260 },
        { x = 2327.70, y = 17412.94, z = 1235.00 },
        "trial_of_sun_power_maptrap_02"
    ),
    make_trial_of_sun_power_maptrap_action(2,
        { x = 2327.70, y = 17412.94, z = 1235.00 },
        { x = 1467.36, y = 17027.65, z = 1235.00 },
        "trial_of_sun_power_maptrap_03"
    ),
    make_trial_of_sun_power_maptrap_action(3,
        { x = 1467.36, y = 17027.65, z = 1235.00 },
        { x = 359.60, y = 16832.20, z = 1235.00 },
        "trial_of_sun_power_maptrap_04"
    ),
    make_trial_of_sun_power_maptrap_action(4,
        { x = 359.60, y = 16832.20, z = 1235.00 },
        { x = -485.17, y = 16812.81, z = 1235.00 },
        "trial_of_sun_power_maptrap_05"
    ),
    make_trial_of_sun_power_maptrap_action(5,
        { x = -485.17, y = 16812.81, z = 1235.00 },
        { x = -1232.50, y = 17075.33, z = 1235.00 },
        "trial_of_sun_power_maptrap_06"
    ),
    make_trial_of_sun_power_maptrap_action(6,
        { x = -1232.50, y = 17075.33, z = 1235.00 },
        { x = -1857.00, y = 17409.00, z = 1235.00 },
        "trial_of_sun_power_maptrap_07"
    ),
    make_trial_of_sun_power_maptrap_action(7,
        { x = -1857.00, y = 17409.00, z = 1235.00 },
        { x = -1559.00, y = 17935.00, z = 1235.00 },
        "trial_of_sun_power_maptrap_08"
    ),
    make_trial_of_sun_power_maptrap_action(8,
        { x = -1559.00, y = 17935.00, z = 1235.00 },
        { x = -2171.19, y = 18278.10, z = 1235.00 },
        "trial_of_sun_power_maptrap_09"
    ),
    make_trial_of_sun_power_maptrap_action(9,
        { x = -2171.19, y = 18278.10, z = 1235.00 },
        { x = -1788.54, y = 19355.02, z = 1235.00 },
        "trial_of_sun_power_maptrap_10"
    ),
    make_trial_of_sun_power_maptrap_action(10,
        { x = -1788.54, y = 19355.02, z = 1235.00 },
        { x = -1007.00, y = 18853.00, z = 1235.00 },
        "trial_of_sun_power_maptrap_11"
    ),
    make_trial_of_sun_power_maptrap_action(11,
        { x = -1007.00, y = 18853.00, z = 1235.00 },
        { x = -601.47, y = 18081.72, z = 1235.00 },
        "trial_of_sun_power_maptrap_12"
    ),
    make_trial_of_sun_power_maptrap_action(12,
        { x = -601.47, y = 18081.72, z = 1235.00 },
        { x = -83.00, y = 18882.00, z = 1235.00 },
        "trial_of_sun_power_maptrap_13"
    ),
    make_trial_of_sun_power_maptrap_action(13,
        { x = -83.00, y = 18882.00, z = 1235.00 },
        { x = 442.74, y = 17996.10, z = 1235.00 },
        "trial_of_sun_power_maptrap_14"
    ),
    make_trial_of_sun_power_maptrap_action(14,
        { x = 442.74, y = 17996.10, z = 1235.00 },
        { x = 960.00, y = 18944.00, z = 1235.00 },
        "trial_of_sun_power_maptrap_15"
    ),
    make_trial_of_sun_power_maptrap_action(15,
        { x = 960.00, y = 18944.00, z = 1235.00 },
        { x = 1453.00, y = 18054.00, z = 1235.00 },
        "trial_of_sun_power_maptrap_16"
    ),
    make_trial_of_sun_power_maptrap_action(16,
        { x = 1453.00, y = 18054.00, z = 1235.00 },
        { x = 1951.12, y = 19277.79, z = 1235.00 },
        "trial_of_sun_power_maptrap_17"
    ),
    make_trial_of_sun_power_maptrap_action(17,
        { x = 1951.12, y = 19277.79, z = 1235.00 },
        { x = 2397.10, y = 18547.67, z = 1235.00 },
        "trial_of_sun_power_maptrap_18"
    ),
    make_trial_of_sun_power_maptrap_action(18,
        { x = 2397.10, y = 18547.67, z = 1235.00, radius = 1400, z_tolerance = 260 },
        { x = 187.00, y = 20535.00, z = 1235.00 },
        "trial_of_sun_power_after_complete_route_542_19390"
    ),
    make_trial_of_sun_recorded_route_action(TRIAL_OF_SUN_POWER_SIDE_KEY, {
        key = "trial_of_sun_power_after_complete_route_542_19390",
        label = "太阳的试炼_权力之试_完成后回程路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "权力之试",
            "通过权力试炼",
            "太阳的试炼",
            "通过三处试炼"
        },
        task_detail_patterns = {
            "通过权力试炼",
            "权力之瞳",
            "通过三处试炼",
            "追击夺走火种"
        },
        trigger = {
            x = 187.00,
            y = 20535.00,
            z = 1235.00,
            radius = 1200,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 95000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "trial_of_sun_power_maptrap_19",
        waypoints = {
            { x = 542.00, y = 19390.00, z = 1235.00 },
            { x = 284.54, y = 18443.24, z = 1235.00 },
            { x = 181.00, y = 17590.00, z = 1235.00 },
            { x = 217.00, y = 16532.00, z = 1235.00 },
            { x = 304.00, y = 15178.00, z = 1235.00 },
            { x = -337.27, y = 14257.98, z = 1235.00 },
            { x = -684.49, y = 13499.35, z = 1235.00 },
            { x = -624.95, y = 12832.60, z = 1235.00 },
            { x = -208.78, y = 12317.41, z = 1235.00 },
            { x = 215.17, y = 11740.75, z = 1235.00 },
            { x = 510.06, y = 11072.73, z = 1235.00 },
            { x = 447.25, y = 10496.95, z = 1235.00 },
            { x = 113.77, y = 9892.80, z = 1235.00 },
            { x = 65.76, y = 9265.65, z = 1235.00 },
            { x = 83.28, y = 8543.96, z = 1235.00 },
            { x = 98.44, y = 7918.40, z = 1235.00 },
            { x = 115.74, y = 7273.61, z = 1235.00 },
            { x = -69.00, y = 5927.00, z = 1235.00 }
        }
    }),
    make_trial_of_sun_power_maptrap_action(19,
        { x = -69.00, y = 5927.00, z = 1235.00, radius = 900, z_tolerance = 260 },
        { x = -69.00, y = 5927.00, z = 1235.00 },
        nil
    ),
    make_trial_of_sun_recorded_route_action(TRIAL_OF_SUN_CONQUEST_SIDE_KEY, {
        key = "trial_of_sun_conquest_side_route_-1518_5437",
        label = "太阳的试炼_征伐之试_起步录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        task_patterns = {
            "征伐之试",
            "通过征伐试炼"
        },
        task_detail_patterns = {
            "通过征伐试炼",
            "征伐之瞳"
        },
        trigger = {
            x = -1518.08,
            y = 5436.81,
            z = 1235.00,
            radius = 1800,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 220000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "trial_of_sun_conquest_maptrap_01",
        waypoints = {
            { x = -1518.08, y = 5436.81, z = 1235.00 },
            { x = -2306.17, y = 4339.89, z = 1235.00 },
            { x = -2972.86, y = 2585.38, z = 1235.00 },
            { x = -3724.89, y = 1918.86, z = 1235.00 },
            { x = -5061.51, y = 1527.61, z = 1235.00 },
            { x = -6406.99, y = 1722.56, z = 1235.00 },
            { x = -6836.56, y = 811.83, z = 1235.00 },
            { x = -6881.03, y = 4.60, z = 1235.00 },
            { x = -7004.25, y = -710.57, z = 1235.00 },
            { x = -7639.75, y = -1214.01, z = 1235.00 },
            { x = -8434.44, y = -1190.43, z = 1235.00 },
            { x = -9146.37, y = -1265.76, z = 1235.00 },
            { x = -9927.98, y = -1577.26, z = 1235.00 },
            { x = -11807.55, y = -2326.90, z = 1235.00 },
            { x = -13712.87, y = -1542.04, z = 1235.00 },
            { x = -11949.96, y = -1479.40, z = 1235.00 },
            { x = -12559.01, y = -1947.71, z = 1235.00 },
            { x = -13024.72, y = -2439.29, z = 1235.00 },
            { x = -12978.74, y = -2952.44, z = 1235.00 },
            { x = -12485.60, y = -3231.15, z = 1235.00 },
            { x = -11934.37, y = -3041.10, z = 1235.00 },
            { x = -11611.03, y = -2558.40, z = 1235.00 },
            { x = -11517.17, y = -2052.34, z = 1235.00 },
            { x = -11788.23, y = -1626.30, z = 1235.00 },
            { x = -12328.69, y = -1569.91, z = 1235.00 },
            { x = -12741.55, y = -1828.70, z = 1235.00 },
            { x = -13056.75, y = -2262.73, z = 1235.00 },
            { x = -13093.05, y = -2736.06, z = 1235.00 },
            { x = -12773.58, y = -2951.50, z = 1235.00 },
            { x = -12332.28, y = -2840.23, z = 1235.00 },
            { x = -12095.79, y = -2504.58, z = 1235.00 },
            { x = -12251.82, y = -2148.72, z = 1235.00 },
            { x = -13696.00, y = -1408.00, z = 1235.00 }
        }
    }),
    make_trial_of_sun_side_maptrap_action({
        prefix = "trial_of_sun_conquest",
        label = "太阳的试炼_征伐之试",
        button_label = "征伐之试",
        index = 1,
        task_patterns = {
            "征伐之试",
            "通过征伐试炼"
        },
        task_detail_patterns = {
            "通过征伐试炼",
            "征伐之瞳"
        },
        from_point = { x = -13712.87, y = -1542.04, z = 1235.00, radius = 900, z_tolerance = 260 },
        objective_point = { x = -13712.87, y = -1542.04, z = 1235.00 },
        followup_route_action_key = "trial_of_sun_conquest_return_route_-12474_-1972",
        combat_pulse_while_waiting = true,
        timeout_ms = 180000,
        probe_retry_ms = 550
    }),
    make_trial_of_sun_recorded_route_action(TRIAL_OF_SUN_CONQUEST_SIDE_KEY, {
        key = "trial_of_sun_conquest_return_route_-12474_-1972",
        label = "太阳的试炼_征伐之试_终段录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "征伐之试",
            "通过征伐试炼",
            "太阳的试炼",
            "通过三处试炼"
        },
        task_detail_patterns = {
            "通过征伐试炼",
            "征伐之瞳",
            "通过三处试炼",
            "追击夺走火种"
        },
        trigger = {
            x = -13712.87,
            y = -1542.04,
            z = 1235.00,
            radius = 1400,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 100000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "trial_of_sun_conquest_maptrap_02",
        waypoints = {
            { x = -12474.47, y = -1972.02, z = 1235.00 },
            { x = -10946.70, y = -1942.12, z = 1235.00 },
            { x = -8227.88, y = -1278.80, z = 1235.00 },
            { x = -7242.27, y = -729.80, z = 1235.00 },
            { x = -6873.36, y = 820.17, z = 1235.00 },
            { x = -6127.84, y = 1522.88, z = 1235.00 },
            { x = -5008.53, y = 1591.88, z = 1235.00 },
            { x = -3885.06, y = 1811.48, z = 1235.00 },
            { x = -3103.83, y = 2204.31, z = 1235.00 },
            { x = -1503.00, y = 3065.00, z = 1235.00 }
        }
    }),
    make_trial_of_sun_side_maptrap_action({
        prefix = "trial_of_sun_conquest",
        label = "太阳的试炼_征伐之试",
        button_label = "征伐之试",
        index = 2,
        task_patterns = {
            "征伐之试",
            "通过征伐试炼",
            "太阳的试炼",
            "通过三处试炼"
        },
        task_detail_patterns = {
            "通过征伐试炼",
            "征伐之瞳",
            "通过三处试炼",
            "追击夺走火种"
        },
        from_point = { x = -1503.00, y = 3065.00, z = 1235.00, radius = 900, z_tolerance = 260 },
        objective_point = { x = -1503.00, y = 3065.00, z = 1235.00 }
    }),
    make_route_point_action({
        key = "mountain_heart_dwarf_king_lift_route_-1188_3326",
        label = "群山之心_阻止矮人王阴谋_电梯前置路径",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "mountain_heart_dwarf_king_lift_button_-1750_4240",
        task_patterns = {
            "群山之心"
        },
        task_detail_patterns = {
            "阻止矮人王的阴谋"
        },
        constraint_mode = "all",
        trigger = {
            x = -1188.00,
            y = 3326.00,
            z = 1166.00,
            radius = 780,
            z_tolerance = 420
        },
        retry_ms = 600000,
        timeout_ms = 18000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        waypoints = {
            { x = -1750.00, y = 4240.00, z = 1121.00 }
        }
    }),
    make_route_point_action({
        key = "mountain_heart_dwarf_king_lift_button_-1750_4240",
        label = "群山之心_阻止矮人王阴谋_电梯按钮",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "群山之心"
        },
        task_detail_patterns = {
            "阻止矮人王的阴谋"
        },
        constraint_mode = "all",
        trigger = {
            x = -1750.00,
            y = 4240.00,
            z = 1121.00,
            radius = 560,
            z_tolerance = 420
        },
        interact_radius = 220,
        probe_retry_ms = 700,
        retry_ms = 3500,
        settle_ms = 5000,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        fallback_interact = true,
        fallback_interact_distance = 260,
        fallback_retry_ms = 2500,
        step = {
            key = "mountain_heart_dwarf_king_lift_btn",
            label = "电梯按钮",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.LiftBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.LiftBtn"
            },
            hint_client_x = 694.204834,
            hint_client_y = 720.439941,
            hint_ratio_x = 0.482087,
            hint_ratio_y = 0.800489,
            hint_max_distance = 100.000,
            prefer_hint_fallback = true
        }
    }),
    make_route_point_action({
        key = "kingdom_end_kings_corridor_lift_2815_-2884",
        label = "\u{738B}\u{56FD}\u{7EC8}\u{9014}_\u{56FD}\u{738B}\u{752C}\u{9053}\u{7535}\u{68AF}",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "\u{738B}\u{56FD}\u{7EC8}\u{9014}",
            "\u{524D}\u{5F80}\u{56FD}\u{738B}\u{752C}\u{9053}",
            "\u{8FDB}\u{5165}\u{56FD}\u{738B}\u{752C}\u{9053}",
            "\u{51B2}\u{8FC7}\u{91CD}\u{56F4}\u{FF0C}\u{7A7F}\u{8D8A}\u{56FD}\u{738B}\u{752C}\u{9053}"
        },
        map_patterns = {
            "\u{963F}\u{745E}\u{5A05}",
            "\u{56FD}\u{738B}\u{752C}\u{9053}"
        },
        trigger = {
            x = 2815.00,
            y = -2884.00,
            z = -1695.92,
            radius = 620,
            z_tolerance = 260
        },
        interact_radius = 120,
        probe_retry_ms = 700,
        retry_ms = 12000,
        settle_ms = 3000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 2500,
        timeout_ms = 18000,
        step = {
            key = "kingdom_end_kings_corridor_lift_btn",
            label = "\u{56FD}\u{738B}\u{752C}\u{9053}\u{7535}\u{68AF}\u{6309}\u{94AE}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.LiftBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.LiftBtn"
            },
            hint_client_x = 694.204834,
            hint_client_y = 720.439941,
            hint_ratio_x = 0.482087,
            hint_ratio_y = 0.800489,
            hint_max_distance = 100.000,
            prefer_hint_fallback = true
        }
    }),
    make_npc_dialogue_route_action({
        key = "listen_keli_dwarf_king_dialogue_8378_17447",
        label = "聆听科里和矮人王的对话_NPC对话",
        task_patterns = {
            "群山之心"
        },
        task_detail_patterns = {
            "聆听科里"
        },
        exclude_task_detail_patterns = {
            "击败矮人王多加尔"
        },
        constraint_mode = "all",
        trigger = {
            x = 7451.00,
            y = 17193.00,
            z = 812.00,
            radius = 520,
            z_tolerance = 260
        },
        retry_ms = 6000,
        pre_action_combat_guard = false,
        dialogue = {
            x = 8378.00,
            y = 17447.00,
            z = 811.00,
            radius = 260,
            interact_radius = 120,
            move_interval_ms = 180,
            z_tolerance = 220,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 180,
            fallback_interact = true
        }
    }),
    make_npc_dialogue_route_action({
        key = "ancient_battlefield_keli_dialogue_12169_-7323",
        label = "上古战场_和科里交谈_高ID最近NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        retry_ms = 600000,
        task_patterns = {
            "\u{4E0A}\u{53E4}\u{6218}\u{573A}"
        },
        task_detail_patterns = {
            "\u{548C}\u{79D1}\u{91CC}\u{4EA4}\u{8C08}"
        },
        constraint_mode = "all",
        trigger = {
            x = 12519.00,
            y = -6853.00,
            z = 609.08,
            radius = 1800,
            z_tolerance = 320
        },
        dialogue = {
            x = 12519.00,
            y = -6853.00,
            z = 609.08,
            radius = 220,
            interact_radius = 140,
            move_interval_ms = 220,
            z_tolerance = 320,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_entity_id_min = 1000,
            npc_search_radius = 1400,
            fallback_interact = false
        }
    }),
    make_route_point_action({
        key = "wall_of_sighs_aria_dialogue_route_18621_-3838",
        label = "wall_of_sighs_aria_dialogue_fixed_route",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "wall_of_sighs_aria_dialogue_npc_18882_-2531",
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
        },
        task_detail_patterns = {
            "\u{548C}\u{963F}\u{745E}\u{5A05}\u{4EA4}\u{8C08}"
        },
        constraint_mode = "all",
        trigger = {
            x = 18621.00,
            y = -3838.00,
            z = 403.00,
            radius = 2600,
            z_tolerance = 320
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        waypoints = {
            { x = 18621.00, y = -3838.00, z = 403.00 },
            { x = 18882.00, y = -2531.00, z = 403.00 }
        }
    }),
    make_npc_dialogue_route_action({
        key = "wall_of_sighs_aria_dialogue_npc_18882_-2531",
        label = "wall_of_sighs_aria_dialogue_fixed_route_npc",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
        },
        task_detail_patterns = {
            "\u{548C}\u{963F}\u{745E}\u{5A05}\u{4EA4}\u{8C08}"
        },
        constraint_mode = "all",
        trigger = {
            x = 18882.00,
            y = -2531.00,
            z = 403.00,
            radius = 420,
            z_tolerance = 320
        },
        retry_ms = 6000,
        dialogue = {
            x = 18882.00,
            y = -2531.00,
            z = 403.00,
            radius = 300,
            interact_radius = 240,
            move_interval_ms = 180,
            z_tolerance = 320,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        },
        jump_followup_route_action_key = "wall_of_sighs_aria_dialogue_after_jump_route_19797_-2126",
        jump_followup_route_action_source = "wall_of_sighs_aria_dialogue_jump_followup"
    }),
    make_route_point_action({
        key = "wall_of_sighs_aria_dialogue_after_jump_route_19797_-2126",
        label = "wall_of_sighs_aria_dialogue_after_jump_route",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        retry_ms = 600000,
        timeout_ms = 16000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        trigger = {
            x = 19797.73,
            y = -2126.16,
            z = 403.00,
            radius = 80,
            z_tolerance = 320
        },
        waypoints = {
            { x = 19797.73, y = -2126.16, z = 403.00 }
        }
    }),
    make_npc_dialogue_route_action({
        key = "free_fire_elsa_dialogue_19020_-6640",
        label = "free_fire_elsa_dialogue_npc",
        task_patterns = {
            "\u{81EA}\u{7531}\u{7684}\u{7130}\u{706B}"
        },
        task_detail_patterns = {
            "\u{548C}\u{4F0A}\u{5C14}\u{838E}\u{4EA4}\u{8C08}"
        },
        constraint_mode = "all",
        trigger = {
            x = 19020.00,
            y = -6640.00,
            z = 606.00,
            radius = 1100,
            z_tolerance = 260
        },
        retry_ms = 6000,
        dialogue = {
            x = 19020.00,
            y = -6640.00,
            z = 606.00,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 180,
            z_tolerance = 260,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    }),
    make_npc_dialogue_route_action({
        key = "long_plan_elsa_intel_dialogue_-2430_5050",
        label = "\u{4ECE}\u{957F}\u{8BA1}\u{8BAE}_\u{548C}\u{4F0A}\u{5C14}\u{838E}\u{4EA4}\u{6D41}\u{60C5}\u{62A5}_NPC\u{5BF9}\u{8BDD}",
        task_patterns = {
            "\u{4ECE}\u{957F}\u{8BA1}\u{8BAE}",
            "\u{8FD4}\u{56DE}\u{9ECE}\u{660E}\u{5723}\u{6240}"
        },
        task_detail_patterns = {
            "\u{548C}\u{4F0A}\u{5C14}\u{838E}\u{4EA4}\u{6D41}\u{60C5}\u{62A5}"
        },
        constraint_mode = "all",
        trigger = {
            x = -2430.00,
            y = 5050.00,
            z = 807.00,
            radius = 900,
            z_tolerance = 280
        },
        retry_ms = 6000,
        dialogue = {
            x = -2430.00,
            y = 5050.00,
            z = 807.00,
            radius = 300,
            interact_radius = 150,
            move_interval_ms = 180,
            z_tolerance = 260,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 650,
            fallback_interact = true
        }
    }),
    make_npc_dialogue_route_action({
        key = "tianqian_ariya_dialogue_200_3970",
        label = "\u{5929}\u{5811}\u{6B67}\u{8DEF}_\u{8BE2}\u{95EE}\u{963F}\u{745E}\u{5A05}\u{60C5}\u{51B5}_NPC\u{5BF9}\u{8BDD}",
        task_patterns = {
            "\u{5929}\u{5811}\u{6B67}\u{8DEF}",
            "\u{8BE2}\u{95EE}\u{963F}\u{745E}\u{5A05}\u{60C5}\u{51B5}"
        },
        task_detail_patterns = {
            "\u{8BE2}\u{95EE}\u{963F}\u{745E}\u{5A05}\u{60C5}\u{51B5}"
        },
        constraint_mode = "all",
        trigger = {
            x = 200.00,
            y = 3970.00,
            z = 2446.00,
            radius = 950,
            z_tolerance = 180
        },
        retry_ms = 6000,
        dialogue = {
            x = 200.00,
            y = 3970.00,
            z = 2446.00,
            radius = 360,
            interact_radius = 180,
            move_interval_ms = 180,
            z_tolerance = 160,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "evening_star_elsmerada_no_path_route_747_453_to_1434_-495",
        label = "\u{665A}\u{661F}\u{5F85}\u{660E}_\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}_\u{65E0}\u{8DEF}\u{5F84}\u{56FA}\u{5B9A}\u{8DEF}\u{7EBF}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        wait_task_path_recover_only = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "evening_star_elsmerada_no_path_npc_dialogue_1434_-495",
        followup_route_action_ignore_retry = true,
        task_patterns = {
            "\u{665A}\u{661F}\u{9886}\u{8896}"
        },
        task_detail_patterns = {
            "\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all",
        trigger = {
            x = 747.00,
            y = 453.00,
            z = 2041.80,
            radius = 1300,
            z_tolerance = 420
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        route_worker_max_points = 3,
        waypoints = {
            { x = 747.00, y = 453.00, z = 2041.80 },
            { x = 1105.67, y = 228.17, z = 2032.93 },
            { x = 1434.00, y = -495.00, z = 2034.47 }
        }
    }),
    make_npc_dialogue_route_action({
        key = "evening_star_elsmerada_no_path_npc_dialogue_1434_-495",
        label = "\u{665A}\u{661F}\u{5F85}\u{660E}_\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}_\u{56FA}\u{5B9A}\u{8DEF}\u{7EBF}\u{540E}NPC\u{5BF9}\u{8BDD}",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        direct_when_task_active = true,
        select_nearest_task_dialogue = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        retry_ms = 6000,
        task_patterns = {
            "\u{665A}\u{661F}\u{9886}\u{8896}"
        },
        task_detail_patterns = {
            "\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all",
        trigger = {
            x = 1434.00,
            y = -495.00,
            z = 2034.47,
            radius = 900,
            z_tolerance = 420
        },
        dialogue = {
            x = 1434.00,
            y = -495.00,
            z = 2034.47,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 220,
            z_tolerance = 420,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    }),
    make_npc_dialogue_route_action({
        key = "evening_star_elsmerada_dialogue_first_-289_-1109",
        label = "\u{665A}\u{661F}\u{5F85}\u{660E}_\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}_\u{76F4}\u{63A5}\u{5BF9}\u{8BDD}",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        direct_when_task_active = true,
        select_nearest_task_dialogue = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "\u{665A}\u{661F}\u{5F85}\u{660E}"
        },
        task_detail_patterns = {
            "\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all",
        retry_ms = 6000,
        dialogue = {
            x = -289.00,
            y = -1109.00,
            z = 2021.38,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 220,
            z_tolerance = 420,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    }),
    make_npc_dialogue_route_action({
        key = "evening_star_elsmerada_dialogue_second_-2337_5316",
        label = "\u{665A}\u{661F}\u{5F85}\u{660E}_\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}_\u{7B2C}\u{4E8C}\u{6BB5}\u{5BF9}\u{8BDD}",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        direct_when_task_active = true,
        select_nearest_task_dialogue = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        retry_ms = 6000,
        task_patterns = {
            "\u{665A}\u{661F}\u{5F85}\u{660E}"
        },
        task_detail_patterns = {
            "\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all",
        dialogue = {
            x = -2337.00,
            y = 5316.00,
            z = 2004.00,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 220,
            z_tolerance = 420,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "evening_star_leave_falling_star_market_no_path_route_-749_1235",
        label = "晚星待明_离开坠星集市前往伊吉部族夺回火种_无路径固定路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        wait_task_path_recover_only = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "晚星待明"
        },
        task_detail_patterns = {
            "离开坠星集市，前往伊吉部族夺回火种"
        },
        constraint_mode = "all",
        trigger = {
            x = -749.61,
            y = 1235.88,
            z = 2004.72,
            radius = 1800,
            z_tolerance = 420
        },
        retry_ms = 600000,
        timeout_ms = 90000,
        waypoint_reach_radius = 240,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        route_worker_max_points = 4,
        waypoints = {
            { x = -749.61, y = 1235.88, z = 2004.72 },
            { x = 404.05, y = 1669.52, z = 2017.83 },
            { x = 1174.85, y = 2002.43, z = 2001.42 },
            { x = 1576.05, y = 2328.83, z = 2004.00 },
            { x = 1824.44, y = 2832.17, z = 2003.00 },
            { x = 1963.21, y = 3236.97, z = 2004.00 },
            { x = 2149.05, y = 3742.19, z = 2009.63 },
            { x = 2377.84, y = 4158.94, z = 2010.08 },
            { x = 2606.82, y = 4566.59, z = 2004.04 },
            { x = 2855.17, y = 5008.79, z = 2001.00 },
            { x = 3053.40, y = 5369.95, z = 2001.00 },
            { x = 3181.16, y = 5736.42, z = 2001.00 },
            { x = 3269.67, y = 6162.66, z = 2001.00 },
            { x = 3283.93, y = 6574.41, z = 2001.00 },
            { x = 3293.45, y = 6994.30, z = 2001.00 },
            { x = 3319.62, y = 7492.80, z = 2001.00 }
        }
    }),
    make_npc_dialogue_route_action({
        key = "breakthrough_talk_to_esmeralda_npc_5626_6589",
        label = "突破重围_与艾丝梅拉达对话_NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        retry_ms = 600000,
        task_patterns = {
            "突破重围"
        },
        task_detail_patterns = {
            "与艾丝梅拉达对话"
        },
        constraint_mode = "all",
        trigger = {
            x = 5626.00,
            y = 6589.00,
            z = 849.00,
            radius = 1800,
            z_tolerance = 420
        },
        dialogue = {
            x = 5626.00,
            y = 6589.00,
            z = 849.00,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 220,
            z_tolerance = 420,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "starlight_lions_heart_counterattack_portal_7622_8484",
        label = "\u{7FA4}\u{661F}\u{4E4B}\u{8F89}_\u{524D}\u{5F80}\u{96C4}\u{72EE}\u{4E4B}\u{5FC3}_\u{4F20}\u{9001}\u{95E8}",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_when_task_unknown = true,
        task_patterns = {
            "\u{7FA4}\u{661F}\u{4E4B}\u{8F89}"
        },
        task_detail_patterns = {
            "\u{524D}\u{5F80}\u{96C4}\u{72EE}\u{4E4B}\u{5FC3}",
            "\u{53C2}\u{4E0E}\u{665A}\u{661F}\u{7684}\u{53CD}\u{653B}"
        },
        trigger = {
            x = 7622.00,
            y = 8484.00,
            z = 849.00,
            radius = 420,
            z_tolerance = 260
        },
        require_destination_match = true,
        destination_match_radius = 900,
        interact_radius = 180,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 4500,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "starlight_lions_heart_counterattack_portal_btn",
            label = "\u{96C4}\u{72EE}\u{4E4B}\u{5FC3}\u{4F20}\u{9001}\u{95E8}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
            },
            hint_client_x = 697.528564,
            hint_client_y = 697.631531,
            hint_ratio_x = 0.484395,
            hint_ratio_y = 0.775146,
            hint_max_distance = 100.000,
            hover_capture_enabled = true,
            hover_capture_client_left = 709.0,
            hover_capture_client_top = 685.0,
            hover_capture_client_right = 745.0,
            hover_capture_client_bottom = 722.0,
            hover_capture_retry_ms = 700
        }
    }),
    make_route_point_action({
        key = "starlight_silver_sand_treasure_entry_-9485_-4658",
        label = "\u{7FA4}\u{661F}\u{4E4B}\u{8F89}_\u{94F6}\u{6C99}\u{8FB9}\u{57CE}\u{85CF}\u{5B9D}\u{5730}\u{5165}\u{53E3}\u{5F15}\u{5BFC}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_when_task_unknown = true,
        allow_when_map_unknown = true,
        skip_when_treasure_completed_key = "treasure_silver_sand_edge_city_entry_-7886_-4560_v1",
        skip_when_player_level_at_least = 60,
        task_patterns = {
            "\u{7FA4}\u{661F}\u{4E4B}\u{8F89}",
            "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{94F6}\u{6C99}\u{8FB9}\u{57CE}",
            "\u{94F6}\u{6C99}\u{8FB9}\u{57CE}"
        },
        task_detail_patterns = {
            "\u{5E2E}\u{52A9}\u{665A}\u{661F}\u{51FB}\u{6E83}\u{4F0A}\u{5409}\u{738B}\u{519B}\u{FF0C}\u{627E}\u{5230}\u{72EE}\u{5FC3}\u{738B}",
            "\u{65B0}"
        },
        map_patterns = {
            "\u{96C4}\u{72EE}\u{4E4B}\u{5FC3}"
        },
        constraint_mode = "all",
        trigger = {
            x = -9485.00,
            y = -4658.00,
            z = 1890.00,
            radius = 950,
            z_tolerance = 520
        },
        retry_ms = 600000,
        timeout_ms = 45000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 520,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -8842.00, y = -3889.00, z = 1892.00 },
            { x = -7886.00, y = -4560.00, z = 1921.62 }
        }
    }),
    make_route_point_action({
        key = "fallout_academy_courtyard_unstuck_11229_15430",
        label = "\u{53CD}\u{76EE}_\u{7A7F}\u{8D8A}\u{5B66}\u{57CE}\u{5EAD}\u{9662}_\u{5EAD}\u{9662}\u{5361}\u{70B9}\u{7EA0}\u{504F}",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{53CD}\u{76EE}"
        },
        task_detail_patterns = {
            "\u{7A7F}\u{8D8A}\u{5B66}\u{57CE}\u{5EAD}\u{9662}"
        },
        map_patterns = {
            "\u{5B66}\u{57CE}\u{5EAD}\u{9662}"
        },
        constraint_mode = "all",
        trigger = {
            x = 11228.66,
            y = 15430.23,
            z = 16.00,
            radius = 180,
            z_tolerance = 180
        },
        retry_ms = 600000,
        timeout_ms = 20000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 180,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 11996.00, y = 15205.00, z = 16.00 }
        }
    }),
    make_route_point_action({
        key = "escape_inner_city_scholar_alley_unstuck_-7343_3287",
        label = "\u{9003}\u{79BB}\u{5185}\u{57CE}\u{533A}_\u{6DF1}\u{5165}\u{5B66}\u{8005}\u{8857}\u{5DF7}_\u{5361}\u{70B9}\u{7EA0}\u{504F}",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{9003}\u{79BB}\u{5185}\u{57CE}\u{533A}"
        },
        task_detail_patterns = {
            "\u{6DF1}\u{5165}\u{5B66}\u{8005}\u{8857}\u{5DF7}"
        },
        constraint_mode = "all",
        trigger = {
            x = -7343.00,
            y = 3287.00,
            z = 1304.00,
            radius = 420,
            z_tolerance = 220
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 200,
        waypoint_z_tolerance = 220,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -7248.00, y = 4270.00, z = 1299.12 },
            { x = -11863.98, y = 4063.33, z = 1004.00 }
        }
    }),
    make_route_point_action({
        key = "fire_treader_academy_depth_unstuck_-2011_-2420",
        label = "\u{8E48}\u{706B}\u{4E4B}\u{4EBA}_\u{5723}\u{5FB7}\u{5170}\u{6DF1}\u{5904}_\u{5361}\u{70B9}\u{7EA0}\u{504F}",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{8E48}\u{706B}\u{4E4B}\u{4EBA}"
        },
        task_detail_patterns = {
            "\u{524D}\u{5F80}\u{5723}\u{5FB7}\u{5170}\u{9B54}\u{6CD5}\u{5B66}\u{9662}\u{6DF1}\u{5904}"
        },
        constraint_mode = "all",
        trigger = {
            x = -2011.04,
            y = -2419.83,
            z = 6.00,
            radius = 650,
            z_tolerance = 260
        },
        retry_ms = 10000,
        timeout_ms = 30000,
        waypoint_reach_radius = 160,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -2437.00, y = -3028.00, z = 6.00 }
        }
    }),
    make_route_point_action({
        key = "another_magic_academy_forbidden_entry_route_-813_-1035",
        label = "\u{53E6}\u{4E00}\u{4E2A}\u{9B54}\u{6CD5}\u{5B66}\u{9662}_\u{51B2}\u{7834}\u{9632}\u{7EBF}_\u{7981}\u{533A}\u{5165}\u{53E3}\u{5F55}\u{5236}\u{8DEF}\u{7EBF}",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{53E6}\u{4E00}\u{4E2A}\u{9B54}\u{6CD5}\u{5B66}\u{9662}"
        },
        task_detail_patterns = {
            "\u{51B2}\u{7834}\u{9632}\u{7EBF}\u{FF0C}\u{62B5}\u{8FBE}\u{7981}\u{533A}\u{5165}\u{53E3}"
        },
        constraint_mode = "all",
        trigger = {
            x = -813.34,
            y = -1034.82,
            z = 5.00,
            radius = 1800,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 120000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -813.34, y = -1034.82, z = 5.00 },
            { x = -666.89, y = 1804.62, z = 5.00 },
            { x = -690.00, y = 3710.00, z = 305.00 },
            { x = -1024.87, y = 5459.95, z = 305.00 }
        }
    }),
    make_route_point_action({
        key = "forgotten_temple_entry_anchor_4467_-970",
        label = "\u{9057}\u{5FD8}\u{79D8}\u{6BBF}_\u{5165}\u{53E3}\u{951A}\u{70B9}",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{9057}\u{5FD8}\u{79D8}\u{6BBF}"
        },
        task_detail_patterns = {
            "\u{524D}\u{5F80}\u{9057}\u{5FD8}\u{79D8}\u{6BBF}"
        },
        constraint_mode = "all",
        trigger = {
            x = 4467.00,
            y = -970.00,
            z = 6.00,
            radius = 2600,
            z_tolerance = 260
        },
        retry_ms = 90000,
        timeout_ms = 30000,
        waypoint_reach_radius = 160,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 4467.00, y = -970.00, z = 6.00 }
        }
    }),
    make_route_point_action({
        key = "forgotten_temple_civilian_dialogue_anchor_1344_-15129",
        label = "\u{9057}\u{5FD8}\u{79D8}\u{6BBF}_\u{4E0E}\u{5E73}\u{6C11}\u{5BF9}\u{8BDD}_\u{5BA4}\u{5185}\u{951A}\u{70B9}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        task_patterns = {
            "\u{9057}\u{5FD8}\u{79D8}\u{6BBF}",
            "\u{4E0E}\u{5E73}\u{6C11}\u{5BF9}\u{8BDD}",
            "\u{88AB}\u{5B9E}\u{9A8C}\u{7684}\u{8001}\u{5934}"
        },
        task_detail_patterns = {
            "\u{4E0E}\u{5E73}\u{6C11}\u{5BF9}\u{8BDD}",
            "\u{88AB}\u{5B9E}\u{9A8C}\u{7684}\u{8001}\u{5934}"
        },
        constraint_mode = "all",
        trigger = {
            x = 1344.00,
            y = -15129.00,
            z = 353.00,
            radius = 2600,
            z_tolerance = 420
        },
        retry_ms = 90000,
        timeout_ms = 30000,
        waypoint_reach_radius = 170,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 1344.00, y = -15129.00, z = 353.00 }
        }
    }),
    make_forgotten_temple_rescue_civilian_route_action(
        "left_10110_-17208",
        10110.00,
        -17208.00,
        323.00
    ),
    make_forgotten_temple_rescue_civilian_route_action(
        "middle_10363_-11180",
        10363.00,
        -11180.00,
        323.00
    ),
    make_forgotten_temple_rescue_civilian_route_action(
        "right_13912_-14194",
        13911.89,
        -14194.49,
        323.00
    ),
    make_route_point_action({
        key = "abyss_below_awakened_temple_deep_route_1270_-5810",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{8FDB}\u{5165}\u{89C9}\u{9192}\u{79D8}\u{6BBF}\u{6DF1}\u{5904}_\u{7EC8}\u{70B9}\u{540E}\u{5F55}\u{5236}\u{8DEF}\u{7EBF}",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{8FDB}\u{5165}\u{89C9}\u{9192}\u{79D8}\u{6BBF}\u{6DF1}\u{5904}"
        },
        constraint_mode = "all",
        require_destination_match = true,
        destination_match_radius = 900,
        trigger = {
            x = 1270.00,
            y = -5810.00,
            z = 503.00,
            radius = 260,
            z_tolerance = 720
        },
        retry_ms = 600000,
        timeout_ms = 70000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 720,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 4340.00, y = -6246.00, z = 491.31 },
            { x = 3072.59, y = -6604.02, z = 503.00 },
            { x = 2037.36, y = -5959.17, z = 503.00 },
            { x = 2980.27, y = -5939.52, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "abyss_below_open_gate_gather_6828_-6416",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{627E}\u{5230}\u{6253}\u{5F00}\u{5927}\u{95E8}\u{7684}\u{65B9}\u{5F0F}_GatherBtn",
        mode = "objective_button_flow_point",
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{627E}\u{5230}\u{6253}\u{5F00}\u{5927}\u{95E8}\u{7684}\u{65B9}\u{5F0F}"
        },
        constraint_mode = "all",
        require_destination_match = true,
        destination_match_radius = 1200,
        trigger = {
            x = 6827.58,
            y = -6415.66,
            z = 505.00,
            radius = 850,
            z_tolerance = 320
        },
        interact_radius = 140,
        probe_retry_ms = 700,
        retry_ms = 60000,
        settle_ms = 1800,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "abyss_below_open_gate_gather_btn",
            label = "\u{6253}\u{5F00}\u{5927}\u{95E8}\u{4EA4}\u{4E92}\u{6309}\u{94AE}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn"
            },
            hint_client_x = 706.204834,
            hint_client_y = 726.439941,
            hint_ratio_x = 0.490420,
            hint_ratio_y = 0.807155,
            hint_max_distance = 100.000,
            settle_ms = 1800,
            task_pos_reject_extra_ms = 3500
        }
    }),
    make_route_point_action({
        key = "abyss_below_open_gate_gather_2934_8490",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{627E}\u{5230}\u{6253}\u{5F00}\u{5927}\u{95E8}\u{7684}\u{65B9}\u{5F0F}_\u{7CBE}\u{786E}GatherBtn_2934_8490",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{627E}\u{5230}\u{6253}\u{5F00}\u{5927}\u{95E8}\u{7684}\u{65B9}\u{5F0F}"
        },
        constraint_mode = "all",
        trigger = {
            x = 2934.29,
            y = 8490.34,
            z = 503.00,
            radius = 1200,
            z_tolerance = 320
        },
        interact_radius = 140,
        probe_retry_ms = 700,
        retry_ms = 600000,
        settle_ms = 2200,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3800,
        step = {
            key = "abyss_below_open_gate_precise_gather_btn",
            label = "\u{6253}\u{5F00}\u{5927}\u{95E8}\u{7CBE}\u{786E}\u{4EA4}\u{4E92}\u{6309}\u{94AE}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn"
            },
            hint_client_x = 690.268860,
            hint_client_y = 656.390015,
            hint_ratio_x = 0.479353,
            hint_ratio_y = 0.729322,
            hint_max_distance = 30.000,
            settle_ms = 2200,
            task_pos_reject_extra_ms = 3800
        }
    }),
    make_route_point_action({
        key = "abyss_below_trace_ryan_anchor_3222_6102_then_main_task",
        label = "abyss_below_trace_ryan_anchor_3222_6102_then_main_task",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_names = {
            "\u{8FFD}\u{5BFB}\u{83B1}\u{5B89}\u{7684}\u{8E2A}\u{8FF9}"
        },
        constraint_mode = "all",
        trigger = {
            x = 3222.00,
            y = 6102.00,
            z = 503.00,
            radius = 1800,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 3222.00, y = 6102.00, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "abyss_below_trace_ryan_anchor_4326_6840_to_2801_8001",
        label = "abyss_below_trace_ryan_anchor_4326_6840_to_2801_8001",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{7EE7}\u{7EED}\u{8FFD}\u{5BFB}\u{83B1}\u{5B89}\u{7684}\u{8E2A}\u{8FF9}",
            "\u{8FFD}\u{5BFB}\u{83B1}\u{5B89}\u{7684}\u{8E2A}\u{8FF9}"
        },
        constraint_mode = "all",
        trigger = {
            x = 4326.42,
            y = 6840.77,
            z = 503.00,
            radius = 700,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 35000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 2801.18, y = 8001.29, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "abyss_below_continue_ryan_trace_route_3920_-6148",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{7EE7}\u{7EED}\u{8FFD}\u{5BFB}\u{83B1}\u{5B89}\u{7684}\u{8E2A}\u{8FF9}_\u{5C40}\u{90E8}\u{5F55}\u{5236}\u{8DEF}\u{7EBF}",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{7EE7}\u{7EED}\u{8FFD}\u{5BFB}\u{83B1}\u{5B89}\u{7684}\u{8E2A}\u{8FF9}"
        },
        constraint_mode = "all",
        trigger = {
            x = 3920.00,
            y = -6148.00,
            z = 503.00,
            radius = 900,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 50000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 3468.00, y = -5451.00, z = 503.00 },
            { x = 2615.00, y = -4557.00, z = 503.00 },
            { x = 1849.00, y = -4458.14, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "abyss_below_ascension_hall_chase_gigon_route_-603_24916",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{8FDB}\u{5165}\u{5347}\u{534E}\u{79D8}\u{6BBF}\u{8FFD}\u{51FB}\u{57FA}\u{5188}_\u{4E24}\u{70B9}\u{5F55}\u{5236}\u{8DEF}\u{7EBF}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{8FDB}\u{5165}\u{5347}\u{534E}\u{79D8}\u{6BBF}\u{FF0C}\u{8FFD}\u{51FB}\u{57FA}\u{5188}"
        },
        constraint_mode = "all",
        trigger = {
            x = -603.00,
            y = 24916.00,
            z = 503.00,
            radius = 1900,
            z_tolerance = 320
        },
        retry_ms = 600000,
        timeout_ms = 35000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        wait_task_refresh_before_reacquire_ms = 10000,
        waypoints = {
            { x = -603.00, y = 24916.00, z = 503.00 },
            { x = 980.00, y = 25022.00, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "abyss_below_principal_derek_dialogue_route_-398_25165_to_1681_25614",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{4E0E}\u{6821}\u{957F}\u{5FB7}\u{91CC}\u{514B}\u{5BF9}\u{8BDD}_\u{4E09}\u{70B9}\u{5F55}\u{5236}\u{8DEF}\u{7EBF}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{4E0E}\u{6821}\u{957F}\u{5FB7}\u{91CC}\u{514B}\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all",
        trigger = {
            x = -398.00,
            y = 25165.00,
            z = 509.41,
            radius = 2600,
            z_tolerance = 420
        },
        retry_ms = 600000,
        timeout_ms = 120000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -447.82, y = 25070.35, z = 505.90 },
            { x = 145.70, y = 25218.96, z = 503.00 },
            { x = 618.58, y = 25278.51, z = 503.00 },
            { x = 970.96, y = 25430.05, z = 503.00 },
            { x = 1340.18, y = 25482.23, z = 503.00 },
            { x = 1204.13, y = 25119.46, z = 503.00 },
            { x = 1035.22, y = 24839.87, z = 503.00 },
            { x = 830.58, y = 24503.98, z = 503.00 },
            { x = 667.46, y = 24221.13, z = 503.00 },
            { x = 499.50, y = 23926.39, z = 503.00 },
            { x = 331.92, y = 23631.02, z = 503.00 },
            { x = 41.99, y = 23237.83, z = 503.00 },
            { x = -193.82, y = 23059.09, z = 503.00 },
            { x = -577.51, y = 23038.10, z = 503.00 },
            { x = -901.93, y = 23176.72, z = 503.00 },
            { x = -1135.16, y = 23400.54, z = 503.00 },
            { x = -1378.11, y = 23660.57, z = 503.00 },
            { x = -1568.38, y = 23891.84, z = 503.00 },
            { x = -1736.04, y = 24258.89, z = 503.00 },
            { x = -1726.02, y = 24584.57, z = 503.00 },
            { x = -1592.40, y = 24906.53, z = 503.00 },
            { x = -1377.27, y = 25062.20, z = 503.00 },
            { x = -1123.37, y = 24907.05, z = 503.00 },
            { x = -975.57, y = 24748.35, z = 503.00 },
            { x = -831.52, y = 24518.91, z = 503.00 },
            { x = -793.47, y = 24232.23, z = 503.00 },
            { x = -937.56, y = 23972.47, z = 503.00 },
            { x = -1147.39, y = 23870.49, z = 503.00 },
            { x = -1373.72, y = 24032.56, z = 503.00 },
            { x = -1496.56, y = 24287.95, z = 503.00 },
            { x = -1483.80, y = 24521.47, z = 503.00 },
            { x = -1313.65, y = 24674.29, z = 503.00 },
            { x = -1076.58, y = 24806.83, z = 503.00 },
            { x = -866.20, y = 24833.44, z = 503.00 },
            { x = -603.49, y = 24696.22, z = 503.00 },
            { x = -434.41, y = 24518.79, z = 503.00 },
            { x = -318.21, y = 24323.46, z = 503.00 },
            { x = -204.33, y = 24123.02, z = 503.00 },
            { x = -238.34, y = 23901.42, z = 503.00 },
            { x = -414.60, y = 23785.28, z = 503.00 },
            { x = -681.11, y = 23806.91, z = 503.00 },
            { x = -920.52, y = 23932.00, z = 503.00 },
            { x = -1029.10, y = 24073.59, z = 503.00 },
            { x = -1074.98, y = 24311.68, z = 503.00 },
            { x = -981.61, y = 24526.16, z = 503.00 },
            { x = -783.71, y = 24667.32, z = 503.00 },
            { x = -536.82, y = 24778.44, z = 503.00 },
            { x = -316.80, y = 24847.04, z = 503.00 },
            { x = 33.39, y = 24864.92, z = 503.00 },
            { x = 326.71, y = 24913.46, z = 503.00 },
            { x = 576.80, y = 25015.63, z = 503.00 },
            { x = 1017.00, y = 25001.00, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "abyss_below_fourth_treasure_entry_5643_-530",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{7B2C}\u{56DB}\u{85CF}\u{5B9D}\u{5730}\u{5165}\u{53E3}\u{5F15}\u{5BFC}",
        mode = "recorded_route_point",
        skip_when_treasure_completed_key = "treasure_fourth_entry_5643_-530_v1",
        skip_when_player_level_at_least = 46,
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{7EE7}\u{7EED}\u{8FFD}\u{5BFB}\u{83B1}\u{5B89}\u{7684}\u{8E2A}\u{8FF9}"
        },
        constraint_mode = "all",
        trigger = {
            x = 5642.69,
            y = -530.40,
            z = 503.00,
            radius = 1800,
            z_tolerance = 520
        },
        retry_ms = 600000,
        timeout_ms = 35000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 520,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 5642.69, y = -530.40, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "abyss_below_level_46_lai_an_reacquire_5546_-583",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_46\u{7EA7}_\u{7EE7}\u{7EED}\u{8FFD}\u{5BFB}\u{83B1}\u{5B89}_\u{5361}\u{70B9}\u{7EA0}\u{504F}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        skip_when_player_level_at_least = 47,
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{7EE7}\u{7EED}\u{8FFD}\u{5BFB}\u{83B1}\u{5B89}\u{7684}\u{8E2A}\u{8FF9}"
        },
        constraint_mode = "all",
        trigger = {
            x = 5546.00,
            y = -583.00,
            z = 503.00,
            radius = 420,
            z_tolerance = 520
        },
        retry_ms = 600000,
        timeout_ms = 25000,
        waypoint_reach_radius = 120,
        waypoint_z_tolerance = 520,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 5055.00, y = -246.00, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "tianqian_guard_cannon_awakened_reentry_anchor_16203_18510",
        label = "\u{5929}\u{5811}\u{6B67}\u{8DEF}_\u{5DE8}\u{70AE}\u{5B88}\u{62A4}\u{8005}_\u{91CD}\u{8FDB}\u{623F}\u{951A}\u{70B9}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        task_patterns = {
            "\u{5929}\u{5811}\u{6B67}\u{8DEF}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{5B88}\u{62A4}\u{5DE8}\u{70AE}\u{7684}\u{89C9}\u{9192}\u{8005}"
        },
        constraint_mode = "all",
        trigger = {
            x = 16203.00,
            y = 18510.00,
            z = 108.44,
            radius = 1800,
            z_tolerance = 320
        },
        retry_ms = 12000,
        timeout_ms = 30000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "tianqian_guard_cannon_awakened_reentry_portal_16203_18510",
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 16203.00, y = 18510.00, z = 108.44 }
        }
    }),
    make_route_point_action({
        key = "tianqian_guard_cannon_awakened_reentry_portal_16203_18510",
        label = "\u{5929}\u{5811}\u{6B67}\u{8DEF}_\u{5DE8}\u{70AE}\u{5B88}\u{62A4}\u{8005}_\u{91CD}\u{8FDB}\u{623F}\u{4F20}\u{9001}\u{95E8}",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "\u{5929}\u{5811}\u{6B67}\u{8DEF}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{5B88}\u{62A4}\u{5DE8}\u{70AE}\u{7684}\u{89C9}\u{9192}\u{8005}"
        },
        constraint_mode = "all",
        trigger = {
            x = 16203.00,
            y = 18510.00,
            z = 108.44,
            radius = 720,
            z_tolerance = 320
        },
        interact_radius = 260,
        probe_retry_ms = 500,
        retry_ms = 1200,
        settle_ms = 2200,
        timeout_ms = 16000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        fallback_interact = true,
        fallback_interact_distance = 280,
        fallback_retry_ms = 1800,
        step = {
            key = "tianqian_guard_cannon_awakened_reentry_portal_btn_16203_18510",
            label = "\u{5929}\u{5811}\u{6B67}\u{8DEF}\u{5DE8}\u{70AE}\u{5B88}\u{62A4}\u{8005}\u{91CD}\u{8FDB}\u{623F}PortalBtn",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
            },
            hint_client_x = 697.204834,
            hint_client_y = 724.439941,
            hint_ratio_x = 0.484170,
            hint_ratio_y = 0.804933,
            hint_max_distance = 180.000
        }
    }),
    make_route_point_action({
        key = "empire_ashes_treasure_entry_-1466_8040",
        label = "\u{5E1D}\u{56FD}\u{4F59}\u{7130}_\u{85CF}\u{5B9D}\u{5730}\u{5165}\u{53E3}\u{5F15}\u{5BFC}",
        mode = "recorded_route_point",
        skip_when_treasure_completed_key = "treasure_empire_ashes_wolf_ambush_entry",
        skip_when_player_level_at_least = 36,
        task_patterns = {
            "\u{5E1D}\u{56FD}\u{4F59}\u{7130}"
        },
        task_detail_patterns = {
            "\u{63A2}\u{7D22}\u{7FA4}\u{72FC}\u{8857}\u{5DF7}",
            "\u{6DF1}\u{5165}\u{7FA4}\u{72FC}\u{8857}\u{5DF7}",
            "\u{7A81}\u{7834}\u{7FA4}\u{72FC}\u{5E2E}\u{4F0F}\u{51FB}"
        },
        constraint_mode = "all",
        trigger = {
            x = -1466.00,
            y = 8040.00,
            z = 606.00,
            radius = 900,
            z_tolerance = 1200
        },
        retry_ms = 8000,
        timeout_ms = 45000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -908.56, y = 9365.29, z = 606.00 }
        }
    }),
    make_route_point_action({
        key = "dragonfall_new_sprout_hill_treasure_entry_13597_15915",
        label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}_\u{65B0}\u{7A57}\u{5C71}\u{4E18}\u{85CF}\u{5B9D}\u{5730}\u{5165}\u{53E3}\u{5F15}\u{5BFC}",
        mode = "recorded_route_point",
        skip_when_treasure_completed_key = "treasure_new_sprout_hill_entry_v3",
        skip_when_player_level_at_least = 14,
        task_patterns = {
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}"
        },
        task_detail_patterns = {
            "\u{5BFB}\u{627E}\u{77EE}\u{4EBA}\u{56FD}\u{5EA6}\u{5165}\u{53E3}"
        },
        constraint_mode = "all",
        trigger = {
            x = 13597.00,
            y = 15915.00,
            z = 5214.00,
            radius = 2600,
            z_tolerance = 700
        },
        retry_ms = 8000,
        timeout_ms = 30000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 13597.00, y = 15915.00, z = 5214.00 }
        }
    }),
    make_route_point_action({
        key = "dragonfall_to_dragonbone_plain_transport_2260_-3471",
        label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}_\u{524D}\u{5F80}\u{9F99}\u{9AA8}\u{5E73}\u{539F}_TransportBtn",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}"
        },
        task_detail_patterns = {
            "\u{524D}\u{5F80}\u{9F99}\u{9AA8}\u{5E73}\u{539F}"
        },
        constraint_mode = "all",
        trigger = {
            x = 2260.89,
            y = -3471.18,
            z = 3251.53,
            radius = 1700,
            z_tolerance = 520
        },
        interact_radius = 180,
        probe_retry_ms = 700,
        retry_ms = 12000,
        settle_ms = 1300,
        timeout_ms = 18000,
        arm_task_entry_action_after_click = true,
        step = {
            key = "dragonfall_to_dragonbone_plain_transport_btn",
            label = "\u{9F99}\u{9AA8}\u{5E73}\u{539F}\u{4F20}\u{9001}\u{5165}\u{53E3}\u{6309}\u{94AE}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.TransportBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.TransportBtn"
            },
            hint_client_x = 696.204834,
            hint_client_y = 724.439941,
            hint_ratio_x = 0.483476,
            hint_ratio_y = 0.804933,
            hint_max_distance = 100.000
        }
    }),
    make_route_point_action({
        key = "dragonfall_to_dragonbone_plain_world_map_-6421_-5492",
        label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}_\u{524D}\u{5F80}\u{9F99}\u{9AA8}\u{5E73}\u{539F}_\u{79D1}\u{91CC}\u{5730}\u{56FE}\u{4F20}\u{9001}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        task_patterns = {
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}"
        },
        task_detail_patterns = {
            "\u{524D}\u{5F80}\u{9F99}\u{9AA8}\u{5E73}\u{539F}"
        },
        constraint_mode = "all",
        trigger = {
            x = -6421.00,
            y = -5492.00,
            z = 1502.53,
            radius = 1800,
            z_tolerance = 520
        },
        retry_ms = 12000,
        timeout_ms = 12000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 520,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        arm_task_entry_action_after_click = true,
        waypoints = {
            { x = -6421.00, y = -5492.00, z = 1502.53 }
        }
    }),
    make_route_point_action({
        key = "dragonbone_griffin_exit_move_to_portal_4368_4495",
        label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}_\u{72EE}\u{9E6B}\u{540E}_\u{79FB}\u{52A8}\u{5230}\u{8FC7}\u{56FE}\u{70B9}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        task_patterns = {
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}",
            "\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"
        },
        constraint_mode = "any",
        trigger = {
            x = 4368.00,
            y = 4495.00,
            z = 1192.00,
            radius = 620,
            z_tolerance = 300
        },
        require_destination_match = true,
        destination_match_radius = 900,
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 240,
        waypoint_z_tolerance = 300,
        move_interval_ms = 180,
        reacquire_retry_ms = 1000,
        complete_without_task_reacquire = true,
        followup_route_action_key = "dragonbone_griffin_exit_portal_4368_4495",
        waypoints = {
            { x = 4368.00, y = 4495.00, z = 1192.00 }
        }
    }),
    make_route_point_action({
        key = "dragonbone_griffin_exit_portal_4368_4495",
        label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}_\u{72EE}\u{9E6B}\u{540E}_\u{8FC7}\u{56FE}\u{4F20}\u{9001}\u{95E8}",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}",
            "\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"
        },
        constraint_mode = "any",
        trigger = {
            x = 4368.00,
            y = 4495.00,
            z = 1192.00,
            radius = 620,
            z_tolerance = 300
        },
        require_destination_match = true,
        destination_match_radius = 900,
        interact_radius = 240,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 4500,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        fallback_interact = true,
        fallback_interact_distance = 280,
        fallback_retry_ms = 2500,
        step = {
            key = "dragonbone_griffin_exit_portal_btn_4368_4495",
            label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}\u{72EE}\u{9E6B}\u{540E}\u{8FC7}\u{56FE}\u{4F20}\u{9001}\u{95E8}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
            },
            hint_client_x = 697.204834,
            hint_client_y = 724.439941,
            hint_ratio_x = 0.484170,
            hint_ratio_y = 0.804933,
            hint_max_distance = 180.000
        }
    }),
    make_npc_dialogue_route_action({
        key = "dragonfall_ask_keli_intel_dialogue_10353_26398",
        label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}_\u{5411}\u{79D1}\u{91CC}\u{8BE2}\u{95EE}\u{60C5}\u{62A5}_NPC\u{5BF9}\u{8BDD}",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        task_patterns = {
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}"
        },
        task_detail_patterns = {
            "\u{5411}\u{79D1}\u{91CC}\u{8BE2}\u{95EE}\u{60C5}\u{62A5}"
        },
        constraint_mode = "all",
        trigger = {
            x = 10353.00,
            y = 26398.00,
            z = 5235.94,
            radius = 1800,
            z_tolerance = 520
        },
        retry_ms = 600000,
        dialogue = {
            x = 10353.00,
            y = 26398.00,
            z = 5235.94,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 220,
            z_tolerance = 520,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "lightless_lost_mine_entry_move_to_portal_10364_26747",
        label = "\u{65E0}\u{5149}\u{56FD}\u{5EA6}_\u{524D}\u{5F80}\u{5931}\u{843D}\u{77FF}\u{6D1E}_\u{79FB}\u{52A8}\u{5230}\u{8FC7}\u{56FE}\u{70B9}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "lightless_lost_mine_entry_portal_10364_26747",
        task_patterns = {
            "\u{65E0}\u{5149}\u{56FD}\u{5EA6}"
        },
        task_detail_patterns = {
            "\u{524D}\u{5F80}\u{5931}\u{843D}\u{77FF}\u{6D1E}"
        },
        constraint_mode = "all",
        trigger = {
            x = 10364.19,
            y = 26746.84,
            z = 5233.96,
            radius = 1800,
            z_tolerance = 520
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 520,
        move_interval_ms = 220,
        waypoints = {
            { x = 10364.19, y = 26746.84, z = 5233.96 }
        }
    }),
    make_route_point_action({
        key = "lightless_lost_mine_entry_portal_10364_26747",
        label = "\u{65E0}\u{5149}\u{56FD}\u{5EA6}_\u{524D}\u{5F80}\u{5931}\u{843D}\u{77FF}\u{6D1E}_\u{8FC7}\u{56FE}\u{4F20}\u{9001}\u{95E8}",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "\u{65E0}\u{5149}\u{56FD}\u{5EA6}"
        },
        task_detail_patterns = {
            "\u{524D}\u{5F80}\u{5931}\u{843D}\u{77FF}\u{6D1E}"
        },
        constraint_mode = "all",
        trigger = {
            x = 10364.19,
            y = 26746.84,
            z = 5233.96,
            radius = 620,
            z_tolerance = 520
        },
        interact_radius = 240,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 4500,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        fallback_interact = true,
        fallback_interact_distance = 280,
        fallback_retry_ms = 2500,
        step = {
            key = "lightless_lost_mine_entry_portal_btn_10364_26747",
            label = "\u{65E0}\u{5149}\u{56FD}\u{5EA6}\u{5931}\u{843D}\u{77FF}\u{6D1E}\u{4F20}\u{9001}\u{95E8}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
            },
            hint_client_x = 697.204834,
            hint_client_y = 724.439941,
            hint_ratio_x = 0.484170,
            hint_ratio_y = 0.804933,
            hint_max_distance = 180.000
        }
    }),
    make_route_point_action({
        key = "old_dusk_portal_20986_21998",
        label = "旧日的黄昏_前往溪地虫谷_传送门",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "\u{65E7}\u{65E5}\u{7684}\u{9EC4}\u{660F}"
        },
        task_detail_patterns = {
            "\u{524D}\u{5F80}\u{6EAA}\u{5730}\u{866B}\u{8C37}"
        },
        constraint_mode = "all",
        trigger = {
            x = 20985.59,
            y = 21998.19,
            z = 920.00,
            radius = 820,
            z_tolerance = 260
        },
        interact_radius = 180,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 4500,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        fallback_interact = true,
        fallback_interact_distance = 260,
        fallback_retry_ms = 2500,
        step = {
            key = "old_dusk_portal_btn",
            label = "\u{65E7}\u{65E5}\u{7684}\u{9EC4}\u{660F}\u{4F20}\u{9001}\u{95E8}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
            },
            hint_client_x = 697.204834,
            hint_client_y = 724.439941,
            hint_ratio_x = 0.484170,
            hint_ratio_y = 0.804933,
            hint_max_distance = 180.000
        }
    }),
    make_npc_dialogue_route_action({
        key = "old_dusk_liv_dialogue_5980_5640",
        label = "旧日的黄昏_与丽芙交谈_NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "旧日的黄昏"
        },
        task_detail_patterns = {
            "尽快找到丽芙",
            "与丽芙交谈"
        },
        constraint_mode = "all",
        trigger = {
            x = 5980.00,
            y = 5640.00,
            z = 1010.00,
            radius = 1200,
            z_tolerance = 320
        },
        retry_ms = 6000,
        dialogue = {
            x = 5980.00,
            y = 5640.00,
            z = 1010.00,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 180,
            z_tolerance = 320,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 760,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "old_dusk_chase_lai_an_route_9782_5765",
        label = "旧日的黄昏_追击觉醒者莱安_局部路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "旧日的黄昏"
        },
        task_detail_patterns = {
            "追击觉醒者莱安"
        },
        constraint_mode = "all",
        trigger = {
            x = 9782.00,
            y = 5765.00,
            z = 1010.00,
            radius = 1700,
            z_tolerance = 320
        },
        retry_ms = 600000,
        timeout_ms = 14000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        reacquire_retry_ms = 1000,
        waypoints = {
            { x = 11099.00, y = 4909.00, z = 1010.00 }
        }
    }),
    make_route_point_action({
        key = "rust_depth_defeat_evil_dragon_move_to_portal_-2348_-4741",
        label = "锈蚀深渊_击败邪龙_先移动到传送门",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "rust_depth_defeat_evil_dragon_portal_-2348_-4741",
        task_patterns = {
            "锈蚀深渊",
            "碎骨巨斧"
        },
        task_detail_patterns = {
            "击败邪龙"
        },
        constraint_mode = "all",
        trigger = {
            x = -2348.04,
            y = -4741.14,
            z = 1146.00,
            radius = 1800,
            z_tolerance = 320
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        waypoints = {
            { x = -2348.04, y = -4741.14, z = 1146.00 }
        }
    }),
    make_route_point_action({
        key = "rust_depth_defeat_evil_dragon_portal_-2348_-4741",
        label = "锈蚀深渊_击败邪龙_传送门",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "锈蚀深渊",
            "碎骨巨斧"
        },
        task_detail_patterns = {
            "击败邪龙"
        },
        constraint_mode = "all",
        trigger = {
            x = -2348.04,
            y = -4741.14,
            z = 1146.00,
            radius = 520,
            z_tolerance = 260
        },
        interact_radius = 220,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 4500,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        fallback_interact = true,
        fallback_interact_distance = 260,
        fallback_retry_ms = 2500,
        step = {
            key = "rust_depth_defeat_evil_dragon_portal_btn",
            label = "锈蚀深渊击败邪龙传送门",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
            },
            hint_client_x = 697.204834,
            hint_client_y = 724.439941,
            hint_ratio_x = 0.484170,
            hint_ratio_y = 0.804933,
            hint_max_distance = 180.000
        }
    }),
    make_route_point_action({
        key = "counterattack_dawn_rescue_gather_19907_21521",
        label = "反击的黎明_解救被困者_交互按钮",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "\u{53CD}\u{51FB}\u{7684}\u{9ECE}\u{660E}"
        },
        task_detail_patterns = {
            "\u{89E3}\u{6551}\u{88AB}\u{56F0}\u{8005}"
        },
        constraint_mode = "all",
        trigger = {
            x = 19998.69,
            y = 21445.52,
            z = 920.00,
            radius = 920,
            z_tolerance = 280
        },
        require_destination_match = true,
        destination_match_radius = 980,
        interact_radius = 220,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 2200,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "rescue_gather_btn",
            label = "\u{89E3}\u{6551}\u{88AB}\u{56F0}\u{8005}\u{6309}\u{94AE}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn"
            },
            hint_client_x = 705.053040,
            hint_client_y = 710.797729,
            hint_ratio_x = 0.489960,
            hint_ratio_y = 0.789775,
            hint_max_distance = 120.000,
            prefer_hint_fallback = true,
            settle_ms = 2200,
            task_pos_reject_extra_ms = 3500
        }
    }),
    make_route_point_action({
        key = "fallen_city_holy_tower_1_gather_-6432_-3838",
        label = "陷落圣城_开启第一座圣光塔_第一塔交互按钮",
        mode = "objective_button_flow_point",
        task_patterns = {
            "\u{9677}\u{843D}\u{5723}\u{57CE}"
        },
        task_detail_patterns = {
            "\u{5F00}\u{542F}\u{7B2C}\u{4E00}\u{5EA7}\u{5723}\u{5149}\u{5854}"
        },
        map_patterns = {
            "\u{8363}\u{8000}\u{5E7F}\u{573A}"
        },
        constraint_mode = "all",
        trigger = {
            x = -6432.00,
            y = -3838.00,
            z = -670.00,
            radius = 560,
            z_tolerance = 260
        },
        require_destination_match = true,
        destination_match_radius = 980,
        interact_radius = 60,
        probe_retry_ms = 800,
        retry_ms = 2500,
        settle_ms = 1500,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        skip_task_info_stability_gate_after_click = true,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "fallen_city_holy_tower_1_map_trap_btn",
            label = "\u{7B2C}\u{4E00}\u{5EA7}\u{5723}\u{5149}\u{5854}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{963F}\u{745E}\u{5A05}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
            distance_min = 168.199816,
            distance_max = 173.199816,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
            },
            hint_client_x = 704.204834,
            hint_client_y = 727.439941,
            hint_ratio_x = 0.489031,
            hint_ratio_y = 0.808267,
            hint_max_distance = 120.000,
            prefer_hint_fallback = true,
            settle_ms = 2200,
            task_pos_reject_extra_ms = 3500
        }
    }),
    make_route_point_action({
        key = "fallen_city_holy_tower_2_gather_-6419_4247",
        label = "陷落圣城_开启第一座圣光塔_第二塔交互按钮",
        mode = "objective_button_flow_point",
        task_patterns = {
            "\u{9677}\u{843D}\u{5723}\u{57CE}"
        },
        task_detail_patterns = {
            "\u{5F00}\u{542F}\u{7B2C}\u{4E00}\u{5EA7}\u{5723}\u{5149}\u{5854}"
        },
        map_patterns = {
            "\u{8363}\u{8000}\u{5E7F}\u{573A}"
        },
        constraint_mode = "all",
        trigger = {
            x = -6419.00,
            y = 4247.00,
            z = -670.00,
            radius = 560,
            z_tolerance = 260
        },
        require_destination_match = true,
        destination_match_radius = 980,
        interact_radius = 60,
        probe_retry_ms = 800,
        retry_ms = 2500,
        settle_ms = 1500,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        skip_task_info_stability_gate_after_click = true,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "fallen_city_holy_tower_2_map_trap_btn",
            label = "\u{7B2C}\u{4E8C}\u{5EA7}\u{5723}\u{5149}\u{5854}\u{6309}\u{94AE}",
            distance_anchor_exact_text = "\u{963F}\u{745E}\u{5A05}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
            distance_min = 168.199816,
            distance_max = 173.199816,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
            },
            hint_client_x = 704.204834,
            hint_client_y = 727.439941,
            hint_ratio_x = 0.489031,
            hint_ratio_y = 0.808267,
            hint_max_distance = 120.000,
            prefer_hint_fallback = true,
            settle_ms = 2200,
            task_pos_reject_extra_ms = 3500
        }
    }),
    make_route_point_action({
        key = "fallen_city_holy_tower_floor214_route_-395_941",
        label = "陷落圣城_开启第一座圣光塔_214层固定路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "fallen_city_floor214_terminal_map_trap_2860_0",
        task_patterns = {
            "\u{9677}\u{843D}\u{5723}\u{57CE}"
        },
        task_detail_patterns = {
            "\u{5F00}\u{542F}\u{7B2C}\u{4E00}\u{5EA7}\u{5723}\u{5149}\u{5854}"
        },
        map_patterns = {
            "\u{8363}\u{8000}\u{5E7F}\u{573A}"
        },
        constraint_mode = "all",
        trigger = {
            x = -394.99,
            y = 941.06,
            z = 214.00,
            radius = 360,
            z_tolerance = 180
        },
        retry_ms = 600000,
        timeout_ms = 150000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 240,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 296.36, y = 813.55, z = 214.00 },
            { x = 930.78, y = 914.50, z = 214.00 },
            { x = 1634.39, y = 751.47, z = 214.00 },
            { x = 1928.64, y = 277.91, z = 214.00 },
            { x = 2003.93, y = -298.18, z = 214.00 },
            { x = 1770.64, y = -686.89, z = 214.00 },
            { x = 1223.81, y = -869.04, z = 214.00 },
            { x = 807.06, y = -884.07, z = 214.00 },
            { x = 445.48, y = -637.05, z = 214.00 },
            { x = 269.64, y = -216.91, z = 214.00 },
            { x = 334.15, y = 253.64, z = 214.00 },
            { x = 571.09, y = 659.15, z = 214.00 },
            { x = 944.78, y = 932.55, z = 214.00 },
            { x = 1351.35, y = 892.19, z = 214.00 },
            { x = 1568.39, y = 528.75, z = 214.00 },
            { x = 1710.22, y = 68.66, z = 214.00 },
            { x = 1670.34, y = -317.36, z = 214.00 },
            { x = 1436.89, y = -658.50, z = 214.00 },
            { x = 1051.75, y = -752.95, z = 214.00 },
            { x = 748.68, y = -511.96, z = 214.00 },
            { x = 603.35, y = -40.72, z = 214.00 },
            { x = 740.73, y = 291.73, z = 214.00 },
            { x = 1117.10, y = 505.58, z = 214.00 },
            { x = 1538.77, y = 510.94, z = 214.00 },
            { x = 1799.66, y = 257.02, z = 214.00 },
            { x = 1868.54, y = -130.99, z = 214.00 },
            { x = 1621.71, y = -431.77, z = 214.00 },
            { x = 1267.51, y = -646.31, z = 214.00 },
            { x = 874.76, y = -617.74, z = 214.00 },
            { x = 615.80, y = -343.45, z = 214.00 },
            { x = 607.60, y = 135.36, z = 214.00 },
            { x = 793.95, y = 465.15, z = 214.00 },
            { x = 1201.58, y = 621.32, z = 214.00 },
            { x = 1453.77, y = 527.16, z = 214.00 },
            { x = 1667.00, y = 144.48, z = 214.00 },
            { x = 1539.85, y = -237.95, z = 214.00 },
            { x = 1144.74, y = -391.97, z = 214.00 },
            { x = 836.94, y = -294.42, z = 214.00 },
            { x = 817.62, y = 113.47, z = 214.00 },
            { x = 1144.63, y = 450.36, z = 214.00 },
            { x = 1523.33, y = 540.28, z = 214.00 },
            { x = 1861.58, y = 427.00, z = 214.00 },
            { x = 1973.35, y = 64.40, z = 214.00 },
            { x = 1772.62, y = -263.85, z = 214.00 },
            { x = 1388.79, y = -281.59, z = 214.00 },
            { x = 1087.23, y = -92.81, z = 214.00 },
            { x = 1342.47, y = 183.81, z = 214.00 },
            { x = 1651.00, y = 184.82, z = 214.00 },
            { x = 2066.83, y = 163.33, z = 214.00 },
            { x = 2485.26, y = 76.59, z = 214.00 },
            { x = 2960.00, y = -32.00, z = 214.00 }
        }
    }),
    make_route_point_action({
        key = "fallen_city_floor214_terminal_map_trap_2860_0",
        label = "fallen_city_floor214_terminal_MapTrapBtn",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "\u{9677}\u{843D}\u{5723}\u{57CE}"
        },
        constraint_mode = "all",
        trigger = {
            x = 2860.00,
            y = 0.00,
            z = 214.00,
            radius = 620,
            z_tolerance = 260
        },
        interact_radius = 180,
        probe_retry_ms = 700,
        retry_ms = 600000,
        settle_ms = 2200,
        timeout_ms = 9000,
        force_task_call_after_transition = true,
        skip_task_info_stability_gate_after_click = true,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "fallen_city_floor214_terminal_map_trap_btn",
            label = "floor214_terminal_MapTrapBtn",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
            },
            hint_client_x = 683.268860,
            hint_client_y = 655.390015,
            hint_ratio_x = 0.474492,
            hint_ratio_y = 0.728211,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true,
            settle_ms = 2200,
            task_pos_reject_extra_ms = 3500
        }
    }),
    make_route_point_action({
        key = "fallen_city_meet_responder_fixed_route_-7429_-3143",
        label = "陷落圣城_与接应者会合_固定路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "fallen_city_meet_responder_npc_dialogue_-6489_-3359",
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "陷落圣城"
        },
        task_detail_patterns = {
            "与接应者会合"
        },
        constraint_mode = "all",
        trigger = {
            x = -7429.00,
            y = -3143.00,
            z = -670.00,
            radius = 1200,
            z_tolerance = 320
        },
        retry_ms = 600000,
        timeout_ms = 140000,
        waypoint_reach_radius = 240,
        waypoint_z_tolerance = 320,
        move_interval_ms = 180,
        reacquire_retry_ms = 1000,
        waypoints = {
            { x = -7732.59, y = -3241.48, z = -670.00 },
            { x = -8048.41, y = -3890.47, z = -670.00 },
            { x = -8458.09, y = -3537.63, z = -670.00 },
            { x = -8453.02, y = -2969.25, z = -670.00 },
            { x = -8398.98, y = -2479.49, z = -670.00 },
            { x = -7968.41, y = -2216.02, z = -670.00 },
            { x = -7512.76, y = -2182.41, z = -670.00 },
            { x = -7171.42, y = -2521.39, z = -670.00 },
            { x = -7132.87, y = -2900.41, z = -670.00 },
            { x = -7216.63, y = -3377.06, z = -670.00 },
            { x = -7561.80, y = -3730.39, z = -670.00 },
            { x = -7977.74, y = -3615.29, z = -670.00 },
            { x = -8310.91, y = -3287.58, z = -670.00 },
            { x = -8405.78, y = -2864.77, z = -670.00 },
            { x = -8259.70, y = -2425.25, z = -670.00 },
            { x = -7934.01, y = -2136.53, z = -670.00 },
            { x = -7522.45, y = -1977.10, z = -670.00 },
            { x = -7103.09, y = -2123.54, z = -670.00 },
            { x = -6923.05, y = -2512.17, z = -670.00 },
            { x = -6959.89, y = -2938.17, z = -670.00 },
            { x = -7171.14, y = -3349.14, z = -670.00 },
            { x = -7514.75, y = -3641.04, z = -670.00 },
            { x = -7983.21, y = -3638.90, z = -670.00 },
            { x = -8186.56, y = -3292.58, z = -670.00 },
            { x = -7918.10, y = -2922.90, z = -670.00 },
            { x = -7539.51, y = -2725.02, z = -670.00 },
            { x = -7092.53, y = -2685.82, z = -670.00 },
            { x = -6676.33, y = -2730.96, z = -670.00 },
            { x = -6238.89, y = -2762.66, z = -670.00 },
            { x = -5936.40, y = -2523.82, z = -670.00 },
            { x = -5995.83, y = -2143.26, z = -670.00 },
            { x = -6320.91, y = -1952.57, z = -670.00 },
            { x = -6759.71, y = -2057.25, z = -670.00 },
            { x = -6968.89, y = -2333.66, z = -670.00 },
            { x = -6977.06, y = -2769.00, z = -670.00 },
            { x = -6835.47, y = -3058.91, z = -670.00 },
            { x = -6473.98, y = -3137.52, z = -670.00 },
            { x = -6245.50, y = -2737.84, z = -670.00 },
            { x = -6306.63, y = -2455.87, z = -670.00 },
            { x = -6675.95, y = -2318.11, z = -670.00 },
            { x = -6511.64, y = -2998.08, z = -670.00 },
            { x = -6488.79, y = -3358.99, z = -670.00 }
        }
    }),
    make_npc_dialogue_route_action({
        key = "fallen_city_meet_responder_npc_dialogue_-6489_-3359",
        label = "陷落圣城_与接应者会合_NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        drop_active_when_task_mismatch = true,
        pre_action_combat_guard = false,
        task_refresh_success_timeout_ms = 30000,
        task_patterns = {
            "陷落圣城"
        },
        task_detail_patterns = {
            "与接应者会合"
        },
        constraint_mode = "all",
        trigger = {
            x = -6488.79,
            y = -3358.99,
            z = -670.00,
            radius = 900,
            z_tolerance = 320
        },
        retry_ms = 6000,
        dialogue = {
            x = -6488.79,
            y = -3358.99,
            z = -670.00,
            radius = 420,
            interact_radius = 190,
            move_interval_ms = 180,
            z_tolerance = 320,
            center_settle_ms = 500,
            interact_retry_ms = 1200,
            timeout_ms = 22000,
            npc_search_radius = 1000,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "lionheart_aria_press_a_then_dialogue_9054_-2058",
        label = "狮心_与阿瑞娅对话_先按A五次",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "与阿瑞娅对话"
        },
        constraint_mode = "all",
        trigger = {
            x = 8916.66,
            y = -1310.97,
            z = 1806.00,
            radius = 2600,
            z_tolerance = 700
        },
        interact_radius = 2600,
        pre_action_combat_guard = false,
        retry_ms = 600000,
        settle_ms = 600,
        timeout_ms = 12000,
        hotkey = "A",
        hotkey_repeat_count = 5,
        hotkey_interval_ms = 180,
        hotkey_label = "lionheart aria pre-dialogue",
        followup_route_action_key = "lionheart_aria_dialogue_9054_-2058"
    }),
    make_npc_dialogue_route_action({
        key = "lionheart_aria_dialogue_9054_-2058",
        label = "狮心_与阿瑞娅对话_NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "与阿瑞娅对话"
        },
        constraint_mode = "all",
        trigger = {
            x = 8916.66,
            y = -1310.97,
            z = 1806.00,
            radius = 1900,
            z_tolerance = 520
        },
        retry_ms = 6000,
        dialogue = {
            x = 8916.66,
            y = -1310.97,
            z = 1806.00,
            radius = 260,
            interact_radius = 160,
            move_interval_ms = 180,
            z_tolerance = 260,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 900,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "eternal_gilding_square_route_-8273_541",
        label = "永恒鎏金_深入永恒广场_录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "永恒鎏金",
            "冠军之路"
        },
        task_detail_patterns = {
            "深入铺满纯金的广场",
            "找到神秘人的下落",
            "询问打开大门的方法"
        },
        constraint_mode = "all",
        trigger = {
            x = -8273.00,
            y = 541.06,
            z = -107.97,
            radius = 1500,
            z_tolerance = 520
        },
        retry_ms = 600000,
        timeout_ms = 160000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 620,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "eternal_gilding_square_npc_dialogue_8494_698",
        waypoints = {
            { x = -8273.00, y = 541.06, z = -107.97 },
            { x = -7257.84, y = 664.85, z = -110.00 },
            { x = -6322.43, y = 726.88, z = 68.79 },
            { x = -5526.75, y = 767.50, z = 464.58 },
            { x = -4732.39, y = 708.99, z = 518.97 },
            { x = -3531.59, y = 735.30, z = 505.00 },
            { x = -3008.99, y = 1099.93, z = 505.00 },
            { x = -2563.65, y = 1633.14, z = 505.00 },
            { x = -2030.90, y = 2299.72, z = 505.00 },
            { x = -1560.45, y = 2619.07, z = 505.00 },
            { x = -1058.58, y = 2441.45, z = 505.00 },
            { x = -679.36, y = 1872.45, z = 505.00 },
            { x = -349.21, y = 1365.66, z = 507.16 },
            { x = 45.57, y = 1066.32, z = 505.00 },
            { x = 856.97, y = 874.55, z = 505.00 },
            { x = 1907.84, y = 694.00, z = 522.46 },
            { x = 2592.42, y = 696.53, z = 816.21 },
            { x = 3200.09, y = 717.16, z = 1092.37 },
            { x = 3774.00, y = 736.67, z = 1107.56 },
            { x = 4979.94, y = 794.64, z = 1200.93 },
            { x = 5553.16, y = 843.23, z = 1491.58 },
            { x = 6196.04, y = 821.32, z = 1724.25 },
            { x = 6805.76, y = 749.64, z = 1716.64 },
            { x = 7278.86, y = 718.26, z = 1710.00 },
            { x = 7857.86, y = 714.32, z = 1710.00 },
            { x = 8422.35, y = 716.95, z = 1714.00 },
            { x = 8493.50, y = 697.54, z = 1714.00 }
        }
    }),
    make_npc_dialogue_route_action({
        key = "eternal_gilding_square_npc_dialogue_8494_698",
        label = "永恒鎏金_永恒广场终点_NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "永恒鎏金",
            "冠军之路"
        },
        task_detail_patterns = {
            "深入铺满纯金的广场",
            "找到神秘人的下落",
            "询问打开大门的方法"
        },
        constraint_mode = "all",
        trigger = {
            x = 8493.50,
            y = 697.54,
            z = 1714.00,
            radius = 1100,
            z_tolerance = 520
        },
        retry_ms = 6000,
        dialogue = {
            x = 8493.50,
            y = 697.54,
            z = 1714.00,
            radius = 300,
            interact_radius = 160,
            move_interval_ms = 180,
            z_tolerance = 320,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 900,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "star_road_detour_route_-1546_2247",
        label = "繁星之路_前往繁星之路_补充录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "繁星之路"
        },
        task_detail_patterns = {
            "前往繁星之路"
        },
        constraint_mode = "all",
        trigger = {
            x = -1546.00,
            y = 2247.47,
            z = 505.00,
            radius = 700,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 70000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -1546.00, y = 2247.47, z = 505.00 },
            { x = -2539.29, y = 1894.69, z = 505.00 },
            { x = -2883.73, y = 647.87, z = 505.00 },
            { x = -2651.73, y = -199.78, z = 505.00 },
            { x = -2131.85, y = -867.56, z = 505.00 },
            { x = -1705.11, y = -1413.48, z = 505.00 }
        }
    }),
    make_route_point_action({
        key = "loser_badge_deep_star_road_spiral_-5397_2352",
        label = "败者之证_深入繁星之路_入口附近固定清图路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "败者之证"
        },
        task_detail_patterns = {
            "深入繁星之路"
        },
        constraint_mode = "all",
        trigger = {
                x = -5397.00,
                y = 2352.00,
                z = 502.00,
                radius = 1800,
                z_tolerance = 260
            },
        retry_ms = 86400000,
        cooldown_on_timeout = true,
        timeout_ms = 240000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -6451.12, y = 2750.30, z = 502.00 },
            { x = -6906.97, y = 3114.04, z = 502.00 },
            { x = -7298.18, y = 3439.45, z = 502.00 },
            { x = -7639.95, y = 3600.41, z = 502.00 },
            { x = -8124.79, y = 3420.52, z = 502.00 },
            { x = -8438.22, y = 3159.65, z = 502.00 },
            { x = -8624.71, y = 2820.44, z = 502.00 },
            { x = -8740.84, y = 2504.53, z = 502.00 },
            { x = -8732.88, y = 2241.60, z = 502.00 },
            { x = -8641.08, y = 1864.93, z = 502.00 },
            { x = -8416.31, y = 1554.80, z = 502.00 },
            { x = -8167.14, y = 1324.85, z = 502.00 },
            { x = -7866.34, y = 1171.15, z = 502.00 },
            { x = -7492.85, y = 1149.36, z = 502.00 },
            { x = -7140.83, y = 1305.91, z = 502.00 },
            { x = -6877.58, y = 1475.87, z = 502.00 },
            { x = -6596.05, y = 1742.56, z = 502.00 },
            { x = -6411.84, y = 1995.71, z = 502.00 },
            { x = -6418.94, y = 2357.13, z = 502.00 },
            { x = -6512.92, y = 2708.50, z = 502.00 },
            { x = -6695.85, y = 3081.23, z = 502.00 },
            { x = -7025.82, y = 3405.15, z = 502.00 },
            { x = -7413.53, y = 3506.94, z = 502.00 },
            { x = -7708.93, y = 3413.58, z = 502.00 },
            { x = -8182.56, y = 3124.50, z = 502.00 },
            { x = -8496.76, y = 2821.57, z = 502.00 },
            { x = -8672.24, y = 2397.06, z = 502.00 },
            { x = -8689.39, y = 1938.01, z = 502.00 },
            { x = -8600.62, y = 1586.82, z = 502.00 },
            { x = -8304.97, y = 1304.93, z = 502.00 },
            { x = -7754.08, y = 1128.53, z = 502.00 },
            { x = -7283.43, y = 1213.74, z = 502.00 },
            { x = -6999.47, y = 1396.17, z = 502.00 },
            { x = -6854.64, y = 1700.05, z = 502.00 },
            { x = -6796.99, y = 2058.44, z = 502.00 },
            { x = -6854.36, y = 2456.38, z = 502.00 },
            { x = -7020.06, y = 2802.14, z = 502.00 },
            { x = -7304.69, y = 3058.52, z = 502.00 },
            { x = -7611.25, y = 3199.37, z = 502.00 },
            { x = -7947.14, y = 3207.53, z = 502.00 },
            { x = -8188.74, y = 3013.19, z = 502.00 },
            { x = -8312.48, y = 2789.33, z = 502.00 },
            { x = -8060.93, y = 2494.02, z = 502.00 },
            { x = -7715.15, y = 2492.73, z = 502.00 },
            { x = -7436.06, y = 2772.22, z = 502.00 },
            { x = -7391.40, y = 3166.94, z = 502.00 },
            { x = -7687.11, y = 3390.02, z = 502.00 },
            { x = -7903.26, y = 3202.44, z = 502.00 },
            { x = -7979.90, y = 2917.22, z = 502.00 },
            { x = -8067.64, y = 2617.26, z = 502.00 },
            { x = -8238.19, y = 2036.08, z = 502.00 },
            { x = -8319.42, y = 1632.53, z = 502.00 },
            { x = -8220.18, y = 1343.49, z = 502.00 },
            { x = -7927.45, y = 1207.21, z = 502.00 },
            { x = -7625.93, y = 1283.98, z = 502.00 },
            { x = -7389.25, y = 1488.35, z = 502.00 },
            { x = -7192.72, y = 1732.11, z = 502.00 },
            { x = -7002.91, y = 1987.36, z = 502.00 },
            { x = -6839.16, y = 2266.09, z = 502.00 },
            { x = -6780.29, y = 2598.89, z = 502.00 },
            { x = -6837.87, y = 2903.11, z = 502.00 },
            { x = -7069.03, y = 3141.67, z = 502.00 },
            { x = -7379.85, y = 3191.38, z = 502.00 },
            { x = -7658.63, y = 3081.38, z = 502.00 },
            { x = -7866.87, y = 2914.15, z = 502.00 },
            { x = -8098.32, y = 2667.22, z = 502.00 },
            { x = -8254.13, y = 2396.05, z = 502.00 },
            { x = -8360.47, y = 2124.65, z = 502.00 },
            { x = -8251.43, y = 1846.10, z = 502.00 },
            { x = -7989.54, y = 1822.31, z = 502.00 },
            { x = -7710.64, y = 1897.36, z = 502.00 },
            { x = -7422.46, y = 2022.30, z = 502.00 },
            { x = -7220.70, y = 2195.68, z = 502.00 },
            { x = -7037.66, y = 2475.82, z = 502.00 },
            { x = -6954.32, y = 2806.71, z = 502.00 },
            { x = -7059.68, y = 3097.64, z = 502.00 },
            { x = -7353.64, y = 3231.67, z = 502.00 },
            { x = -7684.82, y = 3178.71, z = 502.00 },
            { x = -7912.31, y = 3093.52, z = 502.00 },
            { x = -8120.27, y = 2953.28, z = 502.00 },
            { x = -8372.39, y = 2645.92, z = 502.00 },
            { x = -8478.24, y = 2349.13, z = 502.00 },
            { x = -8474.67, y = 2017.51, z = 502.00 },
            { x = -8252.80, y = 1762.65, z = 502.00 },
            { x = -8007.59, y = 1615.50, z = 502.00 },
            { x = -7793.17, y = 1581.08, z = 502.00 },
            { x = -7529.44, y = 1616.44, z = 502.00 },
            { x = -7280.25, y = 1767.62, z = 502.00 },
            { x = -7121.63, y = 2004.82, z = 502.00 },
            { x = -7023.53, y = 2249.94, z = 502.00 },
            { x = -6948.04, y = 2514.97, z = 502.00 },
            { x = -6952.88, y = 2830.34, z = 502.00 },
            { x = -7151.38, y = 3069.56, z = 502.00 },
            { x = -7431.00, y = 3136.56, z = 502.00 },
            { x = -7821.51, y = 3026.85, z = 502.00 },
            { x = -8089.38, y = 2862.81, z = 502.00 },
            { x = -8286.23, y = 2680.06, z = 502.00 },
            { x = -8434.97, y = 2433.31, z = 502.00 },
            { x = -8502.25, y = 2007.94, z = 502.00 },
            { x = -8259.57, y = 1751.46, z = 502.00 },
            { x = -7929.23, y = 1707.09, z = 502.00 },
            { x = -7639.50, y = 1749.09, z = 502.00 },
            { x = -7355.26, y = 1874.65, z = 502.00 },
            { x = -7111.57, y = 2140.03, z = 502.00 },
            { x = -6966.89, y = 2418.86, z = 502.00 },
            { x = -6967.95, y = 2702.29, z = 502.00 },
            { x = -7057.65, y = 2976.06, z = 502.00 },
            { x = -7397.07, y = 3156.70, z = 502.00 },
            { x = -7706.85, y = 3110.52, z = 502.00 },
            { x = -8017.98, y = 2971.40, z = 502.00 },
            { x = -8285.79, y = 2695.27, z = 502.00 },
            { x = -8425.01, y = 2441.43, z = 502.00 },
            { x = -8345.23, y = 2241.58, z = 502.00 },
            { x = -8085.61, y = 2244.72, z = 502.00 },
            { x = -7878.07, y = 2408.20, z = 502.00 },
            { x = -7645.44, y = 2802.46, z = 502.00 },
            { x = -7387.88, y = 2963.09, z = 502.00 },
            { x = -7178.46, y = 2810.30, z = 502.00 },
            { x = -7105.03, y = 2461.08, z = 502.00 },
            { x = -7217.32, y = 2168.80, z = 502.00 },
            { x = -7418.58, y = 1898.78, z = 502.00 },
            { x = -7663.02, y = 1744.99, z = 502.00 },
            { x = -7960.23, y = 1714.82, z = 502.00 },
            { x = -8212.01, y = 1850.67, z = 502.00 },
            { x = -8383.11, y = 2083.84, z = 502.00 },
            { x = -8446.89, y = 2366.35, z = 502.00 },
            { x = -8376.66, y = 2645.74, z = 502.00 },
            { x = -8209.79, y = 2881.60, z = 502.00 },
            { x = -8008.13, y = 3035.00, z = 502.00 },
            { x = -7724.22, y = 3169.15, z = 502.00 },
            { x = -7436.95, y = 3150.26, z = 502.00 },
            { x = -7254.16, y = 2941.32, z = 502.00 },
            { x = -7213.67, y = 2628.70, z = 502.00 },
            { x = -7278.25, y = 2347.08, z = 502.00 },
            { x = -7437.83, y = 2077.74, z = 502.00 },
            { x = -7673.07, y = 1913.95, z = 502.00 },
            { x = -7955.84, y = 1884.97, z = 502.00 },
            { x = -8186.64, y = 2061.22, z = 502.00 },
            { x = -8287.66, y = 2302.92, z = 502.00 },
            { x = -8248.62, y = 2639.68, z = 502.00 },
            { x = -8065.38, y = 2880.58, z = 502.00 },
            { x = -7751.37, y = 3010.78, z = 502.00 },
            { x = -7455.54, y = 3032.97, z = 502.00 },
            { x = -7233.11, y = 2858.28, z = 502.00 },
            { x = -7309.95, y = 2649.29, z = 502.00 },
            { x = -7439.88, y = 2378.15, z = 502.00 },
            { x = -7581.81, y = 2094.18, z = 502.00 },
            { x = -7790.03, y = 1892.56, z = 502.00 },
            { x = -8049.10, y = 1832.81, z = 502.00 },
            { x = -8218.68, y = 2041.46, z = 502.00 },
            { x = -8286.25, y = 2323.25, z = 502.00 },
            { x = -8279.29, y = 2612.22, z = 502.00 },
            { x = -8127.72, y = 2886.50, z = 502.00 },
            { x = -7798.57, y = 3022.01, z = 502.00 },
            { x = -7510.78, y = 3069.38, z = 502.00 },
            { x = -7258.73, y = 2893.67, z = 502.00 },
            { x = -7203.14, y = 2632.15, z = 502.00 },
            { x = -7294.15, y = 2461.67, z = 502.00 },
            { x = -7430.14, y = 2359.39, z = 502.00 },
            { x = -7573.85, y = 2333.61, z = 502.00 },
            { x = -7899.41, y = 2156.87, z = 502.00 },
            { x = -8143.66, y = 2134.99, z = 502.00 },
            { x = -8329.55, y = 2438.71, z = 502.00 },
            { x = -8323.13, y = 2725.60, z = 502.00 },
            { x = -7946.31, y = 3009.74, z = 502.00 },
            { x = -7494.17, y = 2948.77, z = 502.00 },
            { x = -7187.78, y = 2687.62, z = 502.00 },
            { x = -7105.77, y = 2286.80, z = 502.00 },
            { x = -7208.08, y = 1941.03, z = 502.00 },
            { x = -7414.26, y = 1744.36, z = 502.00 },
            { x = -7704.94, y = 1633.46, z = 502.00 },
            { x = -7946.35, y = 1652.37, z = 502.00 },
            { x = -8193.63, y = 1814.71, z = 502.00 },
            { x = -8356.25, y = 2220.08, z = 502.00 },
            { x = -8332.43, y = 2624.20, z = 502.00 },
            { x = -8023.64, y = 2852.41, z = 502.00 },
            { x = -7696.57, y = 2933.02, z = 502.00 },
            { x = -7379.56, y = 2849.36, z = 502.00 },
            { x = -7248.45, y = 2571.58, z = 502.00 },
            { x = -7331.04, y = 2284.01, z = 502.00 },
            { x = -7566.02, y = 2052.86, z = 502.00 },
            { x = -7824.85, y = 1999.58, z = 502.00 }
        }
    }),
    make_route_point_action({
        key = "loser_badge_star_road_hero_loop_-17588_11509",
        label = "败者之证_挑战繁星之路英灵_北侧循环路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "败者之证"
        },
        task_detail_patterns = {
            "挑战繁星之路的英灵"
        },
        constraint_mode = "all",
        trigger = {
            x = -17588.00,
            y = 11509.00,
            z = 502.00,
            radius = 1800,
            z_tolerance = 260
        },
        retry_ms = 86400000,
        cooldown_on_timeout = true,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        route_worker_max_points = 50,
        route_worker_complete_on_path_done = true,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -16836.23, y = 13753.96, z = 502.00 },
            { x = -18266.15, y = 13925.79, z = 502.00 },
            { x = -18377.46, y = 12835.39, z = 502.00 },
            { x = -16746.41, y = 12687.38, z = 502.00 },
            { x = -17468.00, y = 13363.00, z = 502.00 },
            { x = -16836.23, y = 13753.96, z = 502.00 },
            { x = -18266.15, y = 13925.79, z = 502.00 },
            { x = -18377.46, y = 12835.39, z = 502.00 },
            { x = -16746.41, y = 12687.38, z = 502.00 },
            { x = -17468.00, y = 13363.00, z = 502.00 },
            { x = -16836.23, y = 13753.96, z = 502.00 },
            { x = -18266.15, y = 13925.79, z = 502.00 },
            { x = -18377.46, y = 12835.39, z = 502.00 },
            { x = -16746.41, y = 12687.38, z = 502.00 },
            { x = -17468.00, y = 13363.00, z = 502.00 },
            { x = -16836.23, y = 13753.96, z = 502.00 },
            { x = -18266.15, y = 13925.79, z = 502.00 },
            { x = -18377.46, y = 12835.39, z = 502.00 },
            { x = -16746.41, y = 12687.38, z = 502.00 },
            { x = -17468.00, y = 13363.00, z = 502.00 },
            { x = -16836.23, y = 13753.96, z = 502.00 },
            { x = -18266.15, y = 13925.79, z = 502.00 },
            { x = -18377.46, y = 12835.39, z = 502.00 },
            { x = -16746.41, y = 12687.38, z = 502.00 },
            { x = -17468.00, y = 13363.00, z = 502.00 },
            { x = -16836.23, y = 13753.96, z = 502.00 },
            { x = -18266.15, y = 13925.79, z = 502.00 },
            { x = -18377.46, y = 12835.39, z = 502.00 },
            { x = -16746.41, y = 12687.38, z = 502.00 },
            { x = -17468.00, y = 13363.00, z = 502.00 },
            { x = -16836.23, y = 13753.96, z = 502.00 },
            { x = -18266.15, y = 13925.79, z = 502.00 },
            { x = -18377.46, y = 12835.39, z = 502.00 },
            { x = -16746.41, y = 12687.38, z = 502.00 },
            { x = -17468.00, y = 13363.00, z = 502.00 },
            { x = -16836.23, y = 13753.96, z = 502.00 },
            { x = -18266.15, y = 13925.79, z = 502.00 },
            { x = -18377.46, y = 12835.39, z = 502.00 },
            { x = -16746.41, y = 12687.38, z = 502.00 },
            { x = -17468.00, y = 13363.00, z = 502.00 },
            { x = -16836.23, y = 13753.96, z = 502.00 },
            { x = -18266.15, y = 13925.79, z = 502.00 },
            { x = -18377.46, y = 12835.39, z = 502.00 },
            { x = -16746.41, y = 12687.38, z = 502.00 },
            { x = -17468.00, y = 13363.00, z = 502.00 },
            { x = -16836.23, y = 13753.96, z = 502.00 },
            { x = -18266.15, y = 13925.79, z = 502.00 },
            { x = -18377.46, y = 12835.39, z = 502.00 },
            { x = -16746.41, y = 12687.38, z = 502.00 },
            { x = -17468.00, y = 13363.00, z = 502.00 }
        }
    }),
    make_route_point_action({
        key = "loser_badge_star_road_detour_146_12012",
        label = "败者之证_繁星之路_东侧补充录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "败者之证"
        },
        task_detail_patterns = {
            "深入繁星之路",
            "挑战繁星之路的英灵"
        },
        constraint_mode = "all",
        trigger = {
            x = 146.07,
            y = 12012.00,
            z = 502.00,
            radius = 1800,
            z_tolerance = 260
        },
        retry_ms = 86400000,
        cooldown_on_timeout = true,
        timeout_ms = 300000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        route_worker_max_points = 63,
        route_worker_complete_on_path_done = true,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 1432.58, y = 11425.56, z = 502.00 },
            { x = 1923.69, y = 11165.36, z = 502.00 },
            { x = 2385.65, y = 11023.77, z = 502.00 },
            { x = 2757.13, y = 11238.62, z = 502.00 },
            { x = 3001.06, y = 11572.22, z = 502.00 },
            { x = 3229.73, y = 11886.20, z = 502.00 },
            { x = 3270.75, y = 12210.95, z = 502.00 },
            { x = 3074.92, y = 12482.78, z = 502.00 },
            { x = 2719.10, y = 12736.62, z = 502.00 },
            { x = 2396.00, y = 12831.47, z = 502.00 },
            { x = 2075.89, y = 12722.39, z = 502.00 },
            { x = 1813.42, y = 12512.47, z = 502.00 },
            { x = 1632.53, y = 12197.67, z = 502.00 },
            { x = 1564.89, y = 11986.82, z = 502.00 },
            { x = 1652.31, y = 10569.00, z = 502.00 },
            { x = 1835.72, y = 10432.01, z = 502.00 },
            { x = 2118.03, y = 10627.74, z = 502.00 },
            { x = 2418.06, y = 10843.95, z = 502.00 },
            { x = 2684.38, y = 11055.83, z = 502.00 },
            { x = 2925.46, y = 11298.22, z = 502.00 },
            { x = 3085.79, y = 11489.80, z = 502.00 },
            { x = 3273.26, y = 11713.91, z = 502.00 },
            { x = 3378.33, y = 11957.71, z = 502.00 },
            { x = 3328.35, y = 12218.01, z = 502.00 },
            { x = 3140.37, y = 12405.15, z = 502.00 },
            { x = 2877.29, y = 12579.55, z = 502.00 },
            { x = 2576.12, y = 12660.57, z = 502.00 },
            { x = 2310.07, y = 12616.03, z = 502.00 },
            { x = 2053.20, y = 12540.84, z = 502.00 },
            { x = 1789.33, y = 12416.84, z = 502.00 },
            { x = 1601.67, y = 12264.89, z = 502.00 },
            { x = 1424.50, y = 12038.10, z = 502.00 },
            { x = 1337.26, y = 11811.41, z = 502.00 },
            { x = 1362.58, y = 11524.17, z = 502.00 },
            { x = 1470.24, y = 11334.78, z = 502.00 },
            { x = 1662.17, y = 11186.57, z = 502.00 },
            { x = 1920.65, y = 11087.68, z = 502.00 },
            { x = 2211.35, y = 11057.73, z = 502.00 },
            { x = 2477.97, y = 11053.54, z = 502.00 },
            { x = 2729.23, y = 11090.45, z = 502.00 },
            { x = 2934.07, y = 11164.02, z = 502.00 },
            { x = 3100.14, y = 11263.89, z = 502.00 },
            { x = 3225.15, y = 11438.29, z = 502.00 },
            { x = 3297.07, y = 11620.48, z = 502.00 },
            { x = 3316.84, y = 11816.12, z = 502.00 },
            { x = 3297.03, y = 11983.63, z = 502.00 },
            { x = 3231.26, y = 12190.73, z = 502.00 },
            { x = 3133.45, y = 12369.39, z = 502.00 },
            { x = 3004.82, y = 12515.55, z = 502.00 },
            { x = 2828.45, y = 12637.90, z = 502.00 },
            { x = 2666.25, y = 12689.22, z = 502.00 },
            { x = 2428.27, y = 12716.18, z = 502.00 },
            { x = 2169.93, y = 12657.13, z = 502.00 },
            { x = 1987.34, y = 12500.97, z = 502.00 },
            { x = 1850.39, y = 12328.67, z = 502.00 },
            { x = 1738.97, y = 12143.17, z = 502.00 },
            { x = 1659.43, y = 11937.94, z = 502.00 },
            { x = 1608.90, y = 11723.50, z = 502.00 },
            { x = 1610.51, y = 11483.07, z = 502.00 },
            { x = 1713.43, y = 11265.40, z = 502.00 },
            { x = 1906.22, y = 11083.87, z = 502.00 },
            { x = 2090.98, y = 10927.24, z = 502.00 },
            { x = 2309.01, y = 10830.74, z = 502.00 },
            { x = 2530.06, y = 10828.37, z = 502.00 },
            { x = 2777.21, y = 11040.70, z = 502.00 },
            { x = 2922.84, y = 11264.75, z = 502.00 },
            { x = 3030.28, y = 11426.43, z = 502.00 },
            { x = 3137.66, y = 11587.99, z = 502.00 },
            { x = 3266.85, y = 11790.55, z = 502.00 },
            { x = 3309.71, y = 12003.84, z = 502.00 },
            { x = 3248.45, y = 12222.63, z = 502.00 },
            { x = 3100.02, y = 12379.69, z = 502.00 },
            { x = 2935.12, y = 12482.33, z = 502.00 },
            { x = 2758.22, y = 12563.05, z = 502.00 },
            { x = 2549.91, y = 12623.55, z = 502.00 },
            { x = 2380.01, y = 12638.68, z = 502.00 },
            { x = 2190.62, y = 12594.84, z = 502.00 },
            { x = 2022.49, y = 12503.16, z = 502.00 },
            { x = 1876.64, y = 12373.97, z = 502.00 },
            { x = 1759.54, y = 12218.72, z = 502.00 },
            { x = 1654.61, y = 12028.48, z = 502.00 },
            { x = 1578.21, y = 11849.25, z = 502.00 },
            { x = 1536.61, y = 11635.74, z = 502.00 },
            { x = 1534.94, y = 11440.58, z = 502.00 },
            { x = 1594.74, y = 11258.00, z = 502.00 },
            { x = 1745.06, y = 11104.07, z = 502.00 },
            { x = 1931.83, y = 11045.09, z = 502.00 },
            { x = 2099.46, y = 11024.73, z = 502.00 },
            { x = 2305.23, y = 11017.97, z = 502.00 },
            { x = 2572.14, y = 11035.85, z = 502.00 },
            { x = 2786.43, y = 11107.40, z = 502.00 },
            { x = 2952.82, y = 11208.12, z = 502.00 },
            { x = 3083.71, y = 11316.08, z = 502.00 },
            { x = 3193.06, y = 11473.76, z = 502.00 },
            { x = 3270.68, y = 11653.05, z = 502.00 },
            { x = 3329.02, y = 11811.98, z = 502.00 },
            { x = 3384.75, y = 11972.48, z = 502.00 },
            { x = 3430.26, y = 12135.63, z = 502.00 },
            { x = 3454.18, y = 12302.46, z = 502.00 }
        }
    }),
    make_route_point_action({
        key = "loser_badge_star_road_detour_2424_19065",
        label = "败者之证_繁星之路_东南补充录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "败者之证"
        },
        task_detail_patterns = {
            "深入繁星之路",
            "挑战繁星之路的英灵"
        },
        constraint_mode = "all",
        trigger = {
            x = 2424.89,
            y = 19065.02,
            z = 502.00,
            radius = 1800,
            z_tolerance = 260
        },
        retry_ms = 86400000,
        cooldown_on_timeout = true,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        route_worker_max_points = 40,
        route_worker_complete_on_path_done = true,
        reacquire_retry_ms = 1200,
        waypoints = repeat_waypoints({
            { x = 2752.00, y = 20672.00, z = 502.00 },
            { x = 2752.23, y = 22039.44, z = 502.00 },
            { x = 2074.56, y = 21978.63, z = 502.00 },
            { x = 2160.00, y = 21016.00, z = 502.00 }
        }, 10)
    }),
    make_route_point_action({
        key = "loser_badge_star_road_detour_2349_28144",
        label = "败者之证_繁星之路_南侧补充录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "败者之证"
        },
        task_detail_patterns = {
            "深入繁星之路",
            "挑战繁星之路的英灵"
        },
        constraint_mode = "all",
        trigger = {
            x = 2349.00,
            y = 28144.00,
            z = 502.00,
            radius = 1800,
            z_tolerance = 260
        },
        retry_ms = 86400000,
        cooldown_on_timeout = true,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        route_worker_max_points = 20,
        route_worker_complete_on_path_done = true,
        reacquire_retry_ms = 1200,
        waypoints = repeat_waypoints({
            { x = 2284.00, y = 29764.00, z = 502.00 },
            { x = 1538.00, y = 30642.00, z = 502.00 },
            { x = 2424.31, y = 31297.24, z = 502.00 },
            { x = 3491.00, y = 30651.00, z = 502.00 }
        }, 5)
    }),
    make_route_point_action({
        key = "loser_badge_star_road_loop_11216_30722",
        label = "败者之证_繁星之路_终点循环路线直到杰拉尔德任务完成",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        complete_without_task_reacquire = true,
        task_patterns = {
            "败者之证"
        },
        task_detail_patterns = {
            "深入繁星之路",
            "挑战繁星之路的英灵"
        },
        constraint_mode = "all",
        trigger = {
            x = 11216.00,
            y = 30722.00,
            z = 502.00,
            radius = 1800,
            z_tolerance = 260
        },
        retry_ms = 86400000,
        cooldown_on_timeout = true,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        route_worker_max_points = 1,
        route_worker_complete_on_path_done = false,
        waypoints = {
            { x = 12444.96, y = 31111.10, z = 502.00 },
            { x = 13241.18, y = 31864.84, z = 502.00 },
            { x = 14123.90, y = 31250.36, z = 502.00 },
            { x = 14425.36, y = 30325.23, z = 502.00 },
            { x = 13729.57, y = 29756.64, z = 502.00 },
            { x = 12917.99, y = 29985.40, z = 502.00 }
        }
    }),
    make_route_point_action({
        key = "loser_badge_star_road_loop_11216_30722_b",
        label = "败者之证_繁星之路_终点循环路线B直到杰拉尔德任务完成",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        complete_without_task_reacquire = true,
        task_patterns = {
            "败者之证"
        },
        task_detail_patterns = {
            "深入繁星之路",
            "挑战繁星之路的英灵"
        },
        constraint_mode = "all",
        trigger = {
            x = 11216.00,
            y = 30722.00,
            z = 502.00,
            radius = 1800,
            z_tolerance = 260
        },
        retry_ms = 86400000,
        cooldown_on_timeout = true,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        route_worker_max_points = 1,
        route_worker_complete_on_path_done = false,
        waypoints = {
            { x = 12444.96, y = 31111.10, z = 502.00 },
            { x = 13241.18, y = 31864.84, z = 502.00 },
            { x = 14123.90, y = 31250.36, z = 502.00 },
            { x = 14425.36, y = 30325.23, z = 502.00 },
            { x = 13729.57, y = 29756.64, z = 502.00 },
            { x = 12917.99, y = 29985.40, z = 502.00 }
        }
    }),
    make_route_point_action({
        key = "loser_nameless_gerald_route_loop",
        label = "败者无名_杰拉尔德_循环跑打直到任务刷新",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "loser_nameless_gerald_route_loop",
        followup_route_action_ignore_retry = true,
        task_patterns = {
            "败者无名"
        },
        task_detail_patterns = {
            "击败“不洁之星·杰拉尔德”，获得败者之证"
        },
        constraint_mode = "all",
        trigger = {
            x = 12407.09,
            y = 31000.41,
            z = 502.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        route_worker_max_points = 1,
        waypoints = {
            { x = 12407.09, y = 31000.41, z = 502.00 },
            { x = 13545.10, y = 32124.12, z = 502.00 },
            { x = 14637.36, y = 32365.36, z = 502.00 },
            { x = 15669.42, y = 31564.33, z = 502.00 },
            { x = 16191.52, y = 30552.15, z = 502.00 },
            { x = 15628.88, y = 29674.58, z = 502.00 },
            { x = 14810.78, y = 29253.47, z = 502.00 },
            { x = 13907.42, y = 29261.46, z = 502.00 },
            { x = 13103.06, y = 29663.16, z = 502.00 },
            { x = 12537.47, y = 30223.37, z = 502.00 }
        }
    }),
    make_route_point_action({
        key = "face_sun_golden_gate_left_route_-2484_807",
        label = "直面太阳_开启黄金大门_左侧补充路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "直面太阳"
        },
        task_detail_patterns = {
            "开启黄金大门"
        },
        constraint_mode = "all",
        trigger = {
            x = -2484.28,
            y = 807.52,
            z = 505.00,
            radius = 800,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 60000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -2769.00, y = 1921.45, z = 505.00 },
            { x = -1736.00, y = 2412.00, z = 505.00 },
            { x = -574.62, y = 1773.46, z = 505.00 },
            { x = -232.00, y = 865.00, z = 505.00 }
        }
    }),
    make_route_point_action({
        key = "face_sun_golden_gate_detour_-1478_-882",
        label = "直面太阳_开启黄金大门_补充录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "直面太阳"
        },
        task_detail_patterns = {
            "开启黄金大门"
        },
        constraint_mode = "all",
        trigger = {
            x = -1478.42,
            y = -882.43,
            z = 505.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 45000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -1478.42, y = -882.43, z = 505.00 },
            { x = -554.40, y = -340.80, z = 505.00 },
            { x = -103.84, y = 641.28, z = 505.00 }
        }
    }),
    make_route_point_action({
        key = "face_sun_chase_mysterious_person_move_to_portal_12200_699",
        label = "直面太阳_继续追击神秘人_无坐标移动到传送门",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "face_sun_chase_mysterious_person_portal_12200_699",
        task_patterns = {
            "直面太阳"
        },
        task_detail_patterns = {
            "继续追击神秘人"
        },
        constraint_mode = "all",
        trigger = {
            x = 12200.00,
            y = 699.00,
            z = 2330.33,
            radius = 2400,
            z_tolerance = 700
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 240,
        waypoint_z_tolerance = 700,
        move_interval_ms = 220,
        waypoints = {
            { x = 12200.00, y = 699.00, z = 2330.33 }
        }
    }),
    make_route_point_action({
        key = "face_sun_chase_mysterious_person_portal_12200_699",
        label = "直面太阳_继续追击神秘人_传送门过图",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "直面太阳"
        },
        task_detail_patterns = {
            "继续追击神秘人"
        },
        constraint_mode = "all",
        trigger = {
            x = 12200.00,
            y = 699.00,
            z = 2330.33,
            radius = 720,
            z_tolerance = 700
        },
        interact_radius = 260,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 4500,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        fallback_interact = true,
        fallback_interact_distance = 280,
        fallback_retry_ms = 2500,
        step = {
            key = "face_sun_chase_mysterious_person_portal_btn_12200_699",
            label = "直面太阳继续追击神秘人传送门",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
            },
            hint_client_x = 697.204834,
            hint_client_y = 724.439941,
            hint_ratio_x = 0.484170,
            hint_ratio_y = 0.804933,
            hint_max_distance = 180.000
        }
    }),
    make_route_point_action({
        key = "first_light_moon_road_pre_route_-107_1051",
        label = "初升之辉_前往皓月之路_前置录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "初升之辉"
        },
        task_detail_patterns = {
            "前往皓月之路"
        },
        constraint_mode = "all",
        trigger = {
            x = -107.00,
            y = 1051.00,
            z = 506.08,
            radius = 1500,
            z_tolerance = 520
        },
        retry_ms = 600000,
        timeout_ms = 45000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 520,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -107.00, y = 1051.00, z = 506.08 },
            { x = -715.28, y = 1652.45, z = 505.00 },
            { x = -1302.69, y = 2466.81, z = 505.00 }
        }
    }),
    make_route_point_action({
        key = "first_light_moon_road_trial_route_982_20371",
        label = "初升之辉_皓月之路英杰_皓月环路补充路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "初升之辉"
        },
        task_detail_patterns = {
            "踏上皓月之路",
            "挑战路途的英杰"
        },
        constraint_mode = "all",
        trigger = {
            x = 982.00,
            y = 20371.00,
            z = 503.00,
            radius = 1200,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 70000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 1633.38, y = 21080.02, z = 503.00 },
            { x = 2031.96, y = 21636.99, z = 503.00 },
            { x = 2184.92, y = 22240.98, z = 503.00 },
            { x = 1723.54, y = 22694.07, z = 503.00 },
            { x = 1149.17, y = 22845.72, z = 503.00 },
            { x = 597.00, y = 22862.71, z = 503.00 },
            { x = 200.78, y = 22710.52, z = 503.00 },
            { x = -75.82, y = 22320.30, z = 503.00 },
            { x = -167.69, y = 21857.39, z = 503.00 },
            { x = 3.99, y = 21423.49, z = 503.00 },
            { x = 344.65, y = 21071.94, z = 503.00 },
            { x = 794.31, y = 20851.79, z = 503.00 },
            { x = 1269.34, y = 20814.60, z = 503.00 },
            { x = 1710.44, y = 21052.05, z = 503.00 },
            { x = 1957.64, y = 21315.68, z = 503.00 },
            { x = 2150.01, y = 21731.04, z = 503.00 },
            { x = 2123.69, y = 22159.16, z = 503.00 },
            { x = 1901.07, y = 22537.66, z = 503.00 },
            { x = 1536.39, y = 22836.15, z = 503.00 },
            { x = 1122.81, y = 22976.58, z = 503.00 },
            { x = 687.34, y = 22918.55, z = 503.00 },
            { x = 301.98, y = 22650.31, z = 503.00 },
            { x = 50.46, y = 22260.14, z = 503.00 },
            { x = -12.41, y = 21837.45, z = 503.00 },
            { x = 156.08, y = 21397.22, z = 503.00 },
            { x = 479.26, y = 21110.75, z = 503.00 },
            { x = 870.87, y = 20961.99, z = 503.00 },
            { x = 1330.90, y = 20949.50, z = 503.00 },
            { x = 1741.12, y = 21119.36, z = 503.00 },
            { x = 1994.81, y = 21411.29, z = 503.00 },
            { x = 2091.52, y = 21786.15, z = 503.00 },
            { x = 1945.71, y = 22147.23, z = 503.00 },
            { x = 1671.11, y = 22422.38, z = 503.00 },
            { x = 1311.26, y = 22582.20, z = 503.00 },
            { x = 863.42, y = 22586.33, z = 503.00 },
            { x = 503.15, y = 22432.64, z = 503.00 },
            { x = 194.49, y = 22109.75, z = 503.00 },
            { x = -81.68, y = 21719.66, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "first_light_moon_road_trial_route_-8609_21802",
        label = "初升之辉_皓月之路英杰_西侧循环补充路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "初升之辉"
        },
        task_detail_patterns = {
            "踏上皓月之路",
            "挑战路途的英杰"
        },
        constraint_mode = "all",
        trigger = {
            x = -8609.00,
            y = 21802.00,
            z = 503.00,
            radius = 1200,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 70000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -10034.00, y = 22323.00, z = 503.00 },
            { x = -10710.31, y = 22581.39, z = 503.00 },
            { x = -11234.65, y = 22374.94, z = 503.00 },
            { x = -11553.90, y = 21976.20, z = 503.00 },
            { x = -11571.51, y = 21459.53, z = 503.00 },
            { x = -11259.31, y = 21030.15, z = 503.00 },
            { x = -10831.28, y = 20828.29, z = 503.00 },
            { x = -10324.63, y = 20864.94, z = 503.00 },
            { x = -9918.57, y = 21157.24, z = 503.00 },
            { x = -9673.11, y = 21513.66, z = 503.00 },
            { x = -9584.47, y = 21959.93, z = 503.00 },
            { x = -9788.99, y = 22397.36, z = 503.00 },
            { x = -10208.14, y = 22637.89, z = 503.00 },
            { x = -10697.94, y = 22652.55, z = 503.00 },
            { x = -11175.39, y = 22437.82, z = 503.00 },
            { x = -11512.69, y = 22063.71, z = 503.00 },
            { x = -11603.60, y = 21606.63, z = 503.00 },
            { x = -11366.13, y = 21220.50, z = 503.00 },
            { x = -10939.87, y = 20953.52, z = 503.00 },
            { x = -10414.74, y = 20941.17, z = 503.00 },
            { x = -9976.83, y = 21130.01, z = 503.00 },
            { x = -9623.25, y = 21491.33, z = 503.00 },
            { x = -9496.55, y = 21961.16, z = 503.00 },
            { x = -9599.22, y = 22407.90, z = 503.00 },
            { x = -9900.93, y = 22772.89, z = 503.00 },
            { x = -10332.79, y = 22946.28, z = 503.00 },
            { x = -10822.88, y = 22835.33, z = 503.00 },
            { x = -11182.95, y = 22526.77, z = 503.00 },
            { x = -11507.99, y = 22159.25, z = 503.00 },
            { x = -11668.53, y = 21717.28, z = 503.00 },
            { x = -11531.56, y = 21317.87, z = 503.00 },
            { x = -11176.43, y = 20958.95, z = 503.00 },
            { x = -10722.31, y = 20835.84, z = 503.00 },
            { x = -10243.65, y = 20814.57, z = 503.00 },
            { x = -9798.58, y = 20972.47, z = 503.00 },
            { x = -9551.01, y = 21323.22, z = 503.00 },
            { x = -9539.72, y = 21752.67, z = 503.00 },
            { x = -9725.00, y = 22190.85, z = 503.00 },
            { x = -10019.50, y = 22538.77, z = 503.00 },
            { x = -10470.24, y = 22664.38, z = 503.00 },
            { x = -10860.58, y = 22527.10, z = 503.00 },
            { x = -11269.32, y = 22221.27, z = 503.00 },
            { x = -11545.04, y = 21905.25, z = 503.00 },
            { x = -11606.34, y = 21527.47, z = 503.00 },
            { x = -11262.29, y = 21233.60, z = 503.00 },
            { x = -10754.94, y = 21102.28, z = 503.00 },
            { x = -10309.66, y = 21018.44, z = 503.00 },
            { x = -10579.15, y = 20511.65, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "first_light_moon_road_trial_route_-10534_19191",
        label = "初升之辉_皓月之路英杰_北侧循环补充录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "初升之辉"
        },
        task_detail_patterns = {
            "踏上皓月之路",
            "挑战路途的英杰"
        },
        constraint_mode = "all",
        trigger = {
            x = -10533.50,
            y = 19190.70,
            z = 503.00,
            radius = 1200,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 360000,
        cooldown_on_timeout = true,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -10476.16, y = 20073.21, z = 503.00 },
            { x = -10229.26, y = 20730.23, z = 503.00 },
            { x = -9768.41, y = 21249.77, z = 503.00 },
            { x = -9558.03, y = 21813.92, z = 503.00 },
            { x = -9721.13, y = 22361.15, z = 503.00 },
            { x = -10220.59, y = 22683.67, z = 503.00 },
            { x = -10666.91, y = 22540.45, z = 503.00 },
            { x = -11114.28, y = 22447.97, z = 503.00 },
            { x = -11477.95, y = 21969.66, z = 503.00 },
            { x = -11646.44, y = 21510.66, z = 503.00 },
            { x = -11396.55, y = 21126.00, z = 503.00 },
            { x = -10984.09, y = 20867.17, z = 503.00 },
            { x = -10508.52, y = 20725.79, z = 503.00 },
            { x = -10100.24, y = 21081.93, z = 503.00 },
            { x = -9717.42, y = 21363.81, z = 503.00 },
            { x = -9542.47, y = 21830.41, z = 503.00 },
            { x = -9651.80, y = 22287.28, z = 503.00 },
            { x = -9895.40, y = 22618.90, z = 503.00 },
            { x = -10208.79, y = 22695.37, z = 503.00 },
            { x = -10728.34, y = 22743.26, z = 503.00 },
            { x = -11203.05, y = 22525.29, z = 503.00 },
            { x = -11521.49, y = 22159.44, z = 503.00 },
            { x = -11695.56, y = 21709.93, z = 503.00 },
            { x = -11533.63, y = 21278.83, z = 503.00 },
            { x = -11134.20, y = 20894.19, z = 503.00 },
            { x = -10664.40, y = 20640.35, z = 503.00 },
            { x = -10162.02, y = 20862.54, z = 503.00 },
            { x = -9781.77, y = 21227.46, z = 503.00 },
            { x = -9576.90, y = 21651.22, z = 503.00 },
            { x = -9693.32, y = 22113.33, z = 503.00 },
            { x = -9976.82, y = 22522.65, z = 503.00 },
            { x = -10419.00, y = 22721.11, z = 503.00 },
            { x = -10940.67, y = 22577.14, z = 503.00 },
            { x = -11309.21, y = 22239.20, z = 503.00 },
            { x = -11518.92, y = 21771.38, z = 503.00 },
            { x = -11423.91, y = 21309.62, z = 503.00 },
            { x = -11109.29, y = 20959.54, z = 503.00 },
            { x = -10704.66, y = 20616.61, z = 503.00 },
            { x = -10444.25, y = 19963.51, z = 503.00 },
            { x = -10420.64, y = 19728.15, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "first_light_moon_road_trial_route_-15752_5940",
        label = "初升之辉_皓月之路英杰_西南长段补充路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        keep_active_on_task_mismatch = true,
        task_patterns = {
            "初升之辉"
        },
        task_detail_patterns = {
            "踏上皓月之路",
            "挑战路途的英杰"
        },
        constraint_mode = "all",
        trigger = {
            x = -15752.00,
            y = 5940.00,
            z = 503.00,
            radius = 1200,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 360000,
        cooldown_on_timeout = true,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -16001.95, y = 4799.88, z = 503.00 },
            { x = -16558.94, y = 4245.16, z = 503.00 },
            { x = -17336.17, y = 3898.25, z = 503.00 },
            { x = -18045.45, y = 3818.28, z = 503.00 },
            { x = -18671.59, y = 3800.41, z = 503.00 },
            { x = -19387.22, y = 3793.61, z = 503.00 },
            { x = -20005.42, y = 3786.59, z = 503.00 },
            { x = -20652.03, y = 3786.10, z = 503.00 },
            { x = -21252.13, y = 3801.62, z = 503.00 },
            { x = -21897.29, y = 3769.54, z = 503.00 },
            { x = -22485.89, y = 3755.89, z = 503.00 },
            { x = -23411.34, y = 3249.86, z = 503.00 },
            { x = -24077.83, y = 2783.93, z = 503.00 },
            { x = -24845.60, y = 2797.86, z = 503.00 },
            { x = -25200.90, y = 3440.54, z = 503.00 },
            { x = -25202.86, y = 4080.57, z = 503.00 },
            { x = -24852.89, y = 4536.83, z = 503.00 },
            { x = -24209.94, y = 4687.95, z = 503.00 },
            { x = -23649.58, y = 4631.92, z = 503.00 },
            { x = -23181.97, y = 4282.40, z = 503.00 },
            { x = -22975.66, y = 3769.15, z = 503.00 },
            { x = -23098.87, y = 3174.38, z = 503.00 },
            { x = -23553.50, y = 2857.41, z = 503.00 },
            { x = -24091.56, y = 2626.40, z = 503.00 },
            { x = -24569.92, y = 2696.38, z = 503.00 },
            { x = -24777.65, y = 2877.70, z = 503.00 },
            { x = -24939.74, y = 3022.20, z = 503.00 },
            { x = -25361.14, y = 3605.88, z = 503.00 },
            { x = -25269.93, y = 4158.78, z = 503.00 },
            { x = -24957.10, y = 4577.91, z = 503.00 },
            { x = -24452.19, y = 4758.07, z = 503.00 },
            { x = -23933.71, y = 4685.78, z = 503.00 },
            { x = -23479.98, y = 4528.89, z = 503.00 },
            { x = -23116.16, y = 4291.66, z = 503.00 },
            { x = -22914.63, y = 3667.80, z = 503.00 },
            { x = -23391.73, y = 3031.16, z = 503.00 },
            { x = -23896.75, y = 2752.13, z = 503.00 },
            { x = -24423.98, y = 2758.69, z = 503.00 },
            { x = -24863.95, y = 2997.97, z = 503.00 },
            { x = -25171.49, y = 3422.38, z = 503.00 },
            { x = -25293.26, y = 4119.36, z = 503.00 },
            { x = -24972.43, y = 4562.16, z = 503.00 },
            { x = -24492.74, y = 4761.37, z = 503.00 },
            { x = -23933.53, y = 4747.38, z = 503.00 },
            { x = -23656.46, y = 4658.10, z = 503.00 },
            { x = -23441.33, y = 4541.56, z = 503.00 },
            { x = -23265.97, y = 4416.00, z = 503.00 },
            { x = -23042.26, y = 3884.16, z = 503.00 },
            { x = -23268.20, y = 3340.14, z = 503.00 },
            { x = -23658.58, y = 2945.60, z = 503.00 },
            { x = -24080.38, y = 2803.72, z = 503.00 },
            { x = -24554.86, y = 2790.86, z = 503.00 },
            { x = -24862.09, y = 2902.85, z = 503.00 },
            { x = -25202.18, y = 3413.47, z = 503.00 },
            { x = -25212.77, y = 3907.20, z = 503.00 },
            { x = -24929.41, y = 4414.65, z = 503.00 },
            { x = -24489.22, y = 4678.51, z = 503.00 },
            { x = -23867.25, y = 4643.53, z = 503.00 },
            { x = -23496.92, y = 4346.21, z = 503.00 },
            { x = -23245.44, y = 4030.60, z = 503.00 },
            { x = -23149.63, y = 3640.44, z = 503.00 },
            { x = -23419.62, y = 3137.67, z = 503.00 },
            { x = -24089.02, y = 2825.04, z = 503.00 },
            { x = -24627.40, y = 2861.66, z = 503.00 },
            { x = -25025.52, y = 3184.83, z = 503.00 },
            { x = -25156.95, y = 3578.79, z = 503.00 },
            { x = -24889.78, y = 4205.80, z = 503.00 },
            { x = -24408.39, y = 4485.40, z = 503.00 },
            { x = -23778.84, y = 4417.01, z = 503.00 },
            { x = -23288.75, y = 4212.05, z = 503.00 },
            { x = -22790.72, y = 4017.64, z = 503.00 },
            { x = -22301.63, y = 3904.76, z = 503.00 },
            { x = -21689.56, y = 3866.20, z = 503.00 },
            { x = -21096.50, y = 3863.67, z = 503.00 },
            { x = -20153.29, y = 3863.13, z = 503.00 },
            { x = -17229.79, y = 3854.19, z = 503.00 },
            { x = -16358.26, y = 4224.27, z = 503.00 },
            { x = -15743.35, y = 4643.83, z = 503.00 },
            { x = -15084.14, y = 4528.42, z = 503.00 },
            { x = -14658.75, y = 4066.47, z = 503.00 },
            { x = -14332.08, y = 3520.02, z = 503.00 },
            { x = -13797.75, y = 3632.73, z = 503.00 },
            { x = -13167.49, y = 3758.08, z = 503.00 },
            { x = -12465.46, y = 3780.42, z = 503.00 },
            { x = -11769.63, y = 3798.39, z = 503.00 },
            { x = -11069.25, y = 3815.90, z = 503.00 },
            { x = -10341.40, y = 3835.20, z = 503.00 },
            { x = -9583.57, y = 3855.06, z = 503.00 },
            { x = -8868.57, y = 3872.61, z = 503.00 },
            { x = -8291.00, y = 3939.37, z = 503.00 },
            { x = -7696.28, y = 4327.75, z = 503.00 },
            { x = -7283.40, y = 4664.09, z = 503.00 },
            { x = -6696.58, y = 4750.82, z = 503.00 },
            { x = -6267.90, y = 4458.13, z = 503.00 },
            { x = -6045.75, y = 3925.23, z = 503.00 },
            { x = -6357.90, y = 3363.04, z = 503.00 },
            { x = -6668.26, y = 2911.64, z = 503.00 },
            { x = -7092.11, y = 2640.48, z = 503.00 },
            { x = -7785.06, y = 2766.19, z = 503.00 },
            { x = -8262.06, y = 3070.81, z = 503.00 },
            { x = -8444.96, y = 3554.08, z = 503.00 },
            { x = -8234.70, y = 4174.05, z = 503.00 },
            { x = -7795.14, y = 4651.60, z = 503.00 },
            { x = -7269.50, y = 4813.40, z = 503.00 },
            { x = -6704.74, y = 4723.79, z = 503.00 },
            { x = -6221.53, y = 4253.05, z = 503.00 },
            { x = -6208.98, y = 3568.52, z = 503.00 },
            { x = -6565.21, y = 3029.12, z = 503.00 },
            { x = -7033.48, y = 2725.44, z = 503.00 },
            { x = -7631.30, y = 2598.37, z = 503.00 },
            { x = -8089.69, y = 2838.15, z = 503.00 },
            { x = -8355.78, y = 3247.07, z = 503.00 },
            { x = -8506.38, y = 3875.77, z = 503.00 },
            { x = -8232.05, y = 4206.50, z = 503.00 },
            { x = -7834.01, y = 4371.42, z = 503.00 },
            { x = -7421.90, y = 4650.29, z = 503.00 },
            { x = -6932.39, y = 4827.18, z = 503.00 },
            { x = -6646.41, y = 4748.15, z = 503.00 },
            { x = -6250.56, y = 4256.08, z = 503.00 },
            { x = -6303.42, y = 3619.05, z = 503.00 },
            { x = -6647.23, y = 3160.69, z = 503.00 },
            { x = -7165.45, y = 2874.89, z = 503.00 },
            { x = -7677.46, y = 2779.23, z = 503.00 },
            { x = -8072.40, y = 3060.71, z = 503.00 },
            { x = -8258.59, y = 3420.45, z = 503.00 },
            { x = -8208.55, y = 4073.86, z = 503.00 },
            { x = -7766.56, y = 4502.37, z = 503.00 },
            { x = -7406.21, y = 4684.86, z = 503.00 },
            { x = -6867.53, y = 4660.50, z = 503.00 },
            { x = -6392.23, y = 4235.49, z = 503.00 },
            { x = -6324.75, y = 3610.45, z = 503.00 },
            { x = -6678.47, y = 3084.27, z = 503.00 },
            { x = -7044.47, y = 2805.35, z = 503.00 },
            { x = -7435.73, y = 2728.97, z = 503.00 },
            { x = -7705.83, y = 2793.53, z = 503.00 },
            { x = -8135.74, y = 3137.62, z = 503.00 },
            { x = -8451.98, y = 3495.01, z = 503.00 },
            { x = -8933.42, y = 3760.18, z = 503.00 },
            { x = -9480.67, y = 3745.25, z = 503.00 },
            { x = -9937.97, y = 3786.25, z = 503.00 },
            { x = -10475.04, y = 3837.05, z = 503.00 },
            { x = -10952.16, y = 3803.14, z = 503.00 },
            { x = -11479.06, y = 3780.85, z = 503.00 },
            { x = -12067.11, y = 3809.19, z = 503.00 },
            { x = -13613.16, y = 3839.87, z = 503.00 },
            { x = -14356.65, y = 3664.16, z = 503.00 },
            { x = -15244.68, y = 2735.86, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "lionheart_power_facility_chain_route_to_first_maptrap_3797_-1628",
        label = "狮心_关闭供电设施_第一设施固定路线到MapTrap",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "关闭东北边的供电设施"
        },
        constraint_mode = "all",
        trigger = {
            x = 3797.00,
            y = -1628.00,
            z = 2147.38,
            radius = 900,
            z_tolerance = 900
        },
        retry_ms = 600000,
        timeout_ms = 120000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 900,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "lionheart_power_facility_chain_first_maptrap_12949_-6174",
        followup_route_action_ignore_retry = true,
        route_worker_max_points = 36,
        waypoints = {
            { x = 4549.79, y = -1928.20, z = 2123.51 },
            { x = 4742.20, y = -3342.08, z = 2102.22 },
            { x = 4928.76, y = -4338.64, z = 2100.00 },
            { x = 5348.16, y = -5134.84, z = 2100.00 },
            { x = 6164.41, y = -6027.86, z = 2100.00 },
            { x = 6754.14, y = -6375.34, z = 2100.00 },
            { x = 7557.43, y = -6463.89, z = 2100.00 },
            { x = 9053.03, y = -6545.70, z = 2126.63 },
            { x = 9754.41, y = -6589.49, z = 2350.20 },
            { x = 10361.45, y = -6517.15, z = 2618.85 },
            { x = 11004.58, y = -6404.42, z = 2705.11 },
            { x = 11656.45, y = -6367.39, z = 2705.00 },
            { x = 12295.25, y = -6393.70, z = 2706.23 },
            { x = 12892.87, y = -6382.52, z = 2715.72 },
            { x = 12948.68, y = -6173.78, z = 2718.18 },
            { x = 12445.88, y = -6486.02, z = 2708.43 },
            { x = 13176.51, y = -6963.53, z = 2705.00 },
            { x = 13737.16, y = -7054.37, z = 2705.00 },
            { x = 14247.16, y = -6570.80, z = 2705.00 },
            { x = 14169.52, y = -5893.46, z = 2705.00 },
            { x = 13641.65, y = -5491.13, z = 2720.00 },
            { x = 13034.87, y = -5349.08, z = 2716.16 },
            { x = 12730.66, y = -5912.78, z = 2711.22 },
            { x = 12727.24, y = -6606.02, z = 2709.00 },
            { x = 13113.69, y = -7131.96, z = 2705.00 },
            { x = 13714.55, y = -7049.67, z = 2705.00 },
            { x = 14186.54, y = -6753.61, z = 2705.00 },
            { x = 14277.39, y = -6130.39, z = 2705.00 },
            { x = 13854.34, y = -5588.22, z = 2718.37 },
            { x = 13281.61, y = -5490.45, z = 2720.00 },
            { x = 12859.70, y = -5824.71, z = 2714.88 },
            { x = 12700.42, y = -6395.87, z = 2710.00 },
            { x = 12878.57, y = -6999.64, z = 2705.26 },
            { x = 13047.40, y = -7058.19, z = 2705.00 },
            { x = 12641.02, y = -6426.89, z = 2710.00 },
            { x = 12910.87, y = -6327.51, z = 2717.60 }
        }
    }),
    make_route_point_action({
        key = "lionheart_power_facility_chain_first_maptrap_12949_-6174",
        label = "狮心_关闭供电设施_第一设施MapTrapBtn",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        keep_active_on_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "关闭东北边的供电设施",
            "完成"
        },
        constraint_mode = "all",
        trigger = {
            x = 12948.68,
            y = -6173.78,
            z = 2718.18,
            radius = 900,
            z_tolerance = 620
        },
        interact_radius = 220,
        probe_retry_ms = 700,
        retry_ms = 600000,
        settle_ms = 900,
        timeout_ms = 9000,
        force_task_call_after_transition = false,
        followup_route_action_key = "lionheart_power_facility_chain_route_to_second_maptrap_12949_-6174",
        followup_route_action_ignore_retry = true,
        step = {
            key = "lionheart_power_facility_chain_first_map_trap_btn",
            label = "第一供电设施交互按钮",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
            },
            hint_client_x = 704.528564,
            hint_client_y = 712.631531,
            hint_ratio_x = 0.489256,
            hint_ratio_y = 0.791813,
            hint_max_distance = 100.000,
            prefer_hint_fallback = true,
            hover_capture_enabled = true,
            hover_capture_client_left = 690.0,
            hover_capture_client_top = 685.0,
            hover_capture_client_right = 745.0,
            hover_capture_client_bottom = 730.0,
            hover_capture_retry_ms = 700,
            settle_ms = 1800
        }
    }),
    make_route_point_action({
        key = "lionheart_power_facility_chain_route_to_second_maptrap_12949_-6174",
        label = "狮心_关闭供电设施_第二设施固定路线到MapTrap",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        keep_active_on_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "关闭东北边的供电设施",
            "完成"
        },
        constraint_mode = "all",
        trigger = {
            x = 12948.68,
            y = -6173.78,
            z = 2718.18,
            radius = 1800,
            z_tolerance = 900
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 900,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "lionheart_power_facility_chain_second_maptrap_13907_2886",
        followup_route_action_ignore_retry = true,
        route_worker_max_points = 80,
        waypoints = {
            { x = 4549.79, y = -1928.20, z = 2123.51 },
            { x = 4742.20, y = -3342.08, z = 2102.22 },
            { x = 4928.76, y = -4338.64, z = 2100.00 },
            { x = 5348.16, y = -5134.84, z = 2100.00 },
            { x = 6164.41, y = -6027.86, z = 2100.00 },
            { x = 6754.14, y = -6375.34, z = 2100.00 },
            { x = 7557.43, y = -6463.89, z = 2100.00 },
            { x = 9053.03, y = -6545.70, z = 2126.63 },
            { x = 9754.41, y = -6589.49, z = 2350.20 },
            { x = 10361.45, y = -6517.15, z = 2618.85 },
            { x = 11004.58, y = -6404.42, z = 2705.11 },
            { x = 11656.45, y = -6367.39, z = 2705.00 },
            { x = 12295.25, y = -6393.70, z = 2706.23 },
            { x = 12892.87, y = -6382.52, z = 2715.72 },
            { x = 12948.68, y = -6173.78, z = 2718.18 },
            { x = 13637.02, y = -4439.06, z = 2720.00 },
            { x = 13953.30, y = -3456.41, z = 2732.93 },
            { x = 13925.99, y = -2637.40, z = 2810.00 },
            { x = 13867.00, y = -1638.48, z = 2810.00 },
            { x = 13859.05, y = -785.79, z = 2811.44 },
            { x = 13869.10, y = -85.04, z = 2788.80 },
            { x = 13910.44, y = 565.38, z = 2715.60 },
            { x = 13945.91, y = 1113.62, z = 2705.00 },
            { x = 14131.79, y = 1628.94, z = 2705.00 },
            { x = 14426.40, y = 2061.02, z = 2705.00 },
            { x = 14655.59, y = 2555.88, z = 2708.87 },
            { x = 14619.37, y = 3078.95, z = 2735.00 },
            { x = 14416.21, y = 3518.76, z = 2729.58 },
            { x = 13966.02, y = 3635.48, z = 2714.80 },
            { x = 13496.62, y = 3282.68, z = 2718.31 },
            { x = 13033.95, y = 2862.56, z = 2722.04 },
            { x = 12849.45, y = 2365.44, z = 2709.00 },
            { x = 13180.09, y = 1959.63, z = 2705.00 },
            { x = 13584.10, y = 1671.43, z = 2705.00 },
            { x = 14048.86, y = 1638.91, z = 2705.00 },
            { x = 14460.26, y = 1912.60, z = 2705.00 },
            { x = 14607.29, y = 2406.92, z = 2705.11 },
            { x = 14551.45, y = 2991.77, z = 2734.49 },
            { x = 14342.70, y = 3439.81, z = 2730.43 },
            { x = 13902.49, y = 3573.40, z = 2713.83 },
            { x = 13467.09, y = 3246.77, z = 2720.19 },
            { x = 13102.64, y = 2834.97, z = 2723.35 },
            { x = 13002.21, y = 2397.51, z = 2709.00 },
            { x = 13329.97, y = 1924.22, z = 2705.00 },
            { x = 13704.37, y = 1667.09, z = 2705.00 },
            { x = 14130.99, y = 1800.90, z = 2705.00 },
            { x = 14483.14, y = 2116.74, z = 2705.00 },
            { x = 14669.85, y = 2584.07, z = 2709.82 },
            { x = 14531.25, y = 2998.15, z = 2734.69 },
            { x = 14112.99, y = 3329.69, z = 2716.06 },
            { x = 13631.90, y = 3271.26, z = 2714.12 },
            { x = 13258.94, y = 3029.08, z = 2727.84 },
            { x = 12983.20, y = 2702.68, z = 2715.95 },
            { x = 12987.80, y = 2252.66, z = 2708.00 },
            { x = 13458.65, y = 1898.36, z = 2705.00 },
            { x = 14287.80, y = 1894.83, z = 2705.00 },
            { x = 14562.69, y = 2743.99, z = 2719.50 },
            { x = 14142.70, y = 3337.38, z = 2717.92 },
            { x = 13478.41, y = 3355.41, z = 2721.08 },
            { x = 12961.80, y = 2924.77, z = 2721.76 },
            { x = 12935.79, y = 2320.33, z = 2708.51 },
            { x = 13284.36, y = 1893.00, z = 2705.00 },
            { x = 14054.44, y = 1640.28, z = 2705.00 },
            { x = 14598.25, y = 2058.70, z = 2705.00 },
            { x = 14558.44, y = 2785.01, z = 2722.06 },
            { x = 14079.17, y = 3361.65, z = 2713.95 },
            { x = 13420.07, y = 3338.80, z = 2721.79 },
            { x = 13004.42, y = 2841.96, z = 2720.61 },
            { x = 13210.57, y = 2127.46, z = 2705.24 },
            { x = 13536.17, y = 1746.65, z = 2705.00 },
            { x = 14153.67, y = 1845.91, z = 2705.00 },
            { x = 14510.59, y = 2228.82, z = 2705.00 },
            { x = 14434.84, y = 2904.32, z = 2729.11 },
            { x = 13906.56, y = 2885.56, z = 2705.00 }
        }
    }),
    make_route_point_action({
        key = "lionheart_power_facility_chain_second_maptrap_13907_2886",
        label = "狮心_关闭供电设施_第二设施MapTrapBtn",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "关闭东北边的供电设施",
            "完成"
        },
        constraint_mode = "all",
        trigger = {
            x = 13906.56,
            y = 2885.56,
            z = 2705.00,
            radius = 1000,
            z_tolerance = 650
        },
        interact_radius = 220,
        probe_retry_ms = 700,
        retry_ms = 600000,
        settle_ms = 2600,
        timeout_ms = 9000,
        force_task_call_after_transition = false,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "lionheart_power_facility_chain_second_map_trap_btn",
            label = "第二供电设施交互按钮",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
            },
            hint_client_x = 704.528564,
            hint_client_y = 712.631531,
            hint_ratio_x = 0.489256,
            hint_ratio_y = 0.791813,
            hint_max_distance = 100.000,
            prefer_hint_fallback = true,
            hover_capture_enabled = true,
            hover_capture_client_left = 690.0,
            hover_capture_client_top = 685.0,
            hover_capture_client_right = 745.0,
            hover_capture_client_bottom = 730.0,
            hover_capture_retry_ms = 700,
            settle_ms = 1800,
            task_pos_reject_extra_ms = 3500
        }
    }),
    make_route_point_action({
        key = "lionheart_complete_no_path_route_6057_-1283",
        label = "狮心_完成无路径补充路线到第二设施",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        wait_task_path_recover_only = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "完成"
        },
        constraint_mode = "all",
        trigger = {
            x = 6056.99,
            y = -1282.96,
            z = 2108.68,
            radius = 1500,
            z_tolerance = 700
        },
        retry_ms = 600000,
        timeout_ms = 90000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 900,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "lionheart_power_facility_2_gather_13442_2735",
        followup_route_action_ignore_retry = true,
        route_worker_max_points = 22,
        waypoints = {
            { x = 5364.00, y = -1474.00, z = 2111.88 },
            { x = 5181.17, y = -76.84, z = 2105.20 },
            { x = 4945.10, y = 1390.09, z = 2100.00 },
            { x = 5441.39, y = 2329.67, z = 2174.56 },
            { x = 7590.21, y = 3340.81, z = 2409.00 },
            { x = 9879.15, y = 3213.33, z = 2523.47 },
            { x = 10976.06, y = 2998.48, z = 2705.15 },
            { x = 11956.47, y = 3007.02, z = 2705.00 },
            { x = 12875.15, y = 2934.92, z = 2719.21 },
            { x = 13414.39, y = 2457.55, z = 2706.00 },
            { x = 13677.33, y = 1728.73, z = 2705.00 },
            { x = 14681.58, y = 2041.56, z = 2705.00 },
            { x = 14505.21, y = 2537.92, z = 2708.10 },
            { x = 14455.46, y = 2867.73, z = 2727.23 },
            { x = 13977.68, y = 3825.77, z = 2720.33 },
            { x = 14600.07, y = 4142.84, z = 2709.00 },
            { x = 14896.42, y = 3355.05, z = 2734.92 },
            { x = 14690.25, y = 2565.38, z = 2709.17 },
            { x = 14248.68, y = 1520.65, z = 2705.00 },
            { x = 13439.20, y = 1906.45, z = 2705.00 },
            { x = 12923.42, y = 2453.74, z = 2710.00 },
            { x = 13480.40, y = 2739.16, z = 2709.41 }
        }
    }),
    make_route_point_action({
        key = "lionheart_power_facility_1_complete_bridge_route_3880_-1580",
        label = "狮心_关闭供电设施_第一设施完成后补充路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "完成"
        },
        constraint_mode = "all",
        trigger = {
            x = 3880.00,
            y = -1580.00,
            z = 2138.19,
            radius = 900,
            z_tolerance = 700
        },
        retry_ms = 600000,
        timeout_ms = 90000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 900,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "lionheart_power_facility_2_gather_13442_2735",
        waypoints = {
            { x = 4509.32, y = -961.19, z = 2129.90 },
            { x = 5084.05, y = 212.03, z = 2100.00 },
            { x = 5145.04, y = 1119.41, z = 2101.00 },
            { x = 5484.63, y = 2082.42, z = 2148.96 },
            { x = 6233.38, y = 2721.78, z = 2373.28 },
            { x = 8294.40, y = 3494.74, z = 2409.79 },
            { x = 10207.00, y = 3096.69, z = 2629.10 },
            { x = 11083.28, y = 2941.20, z = 2705.00 },
            { x = 11897.81, y = 2962.26, z = 2705.00 },
            { x = 13441.90, y = 2734.86, z = 2712.25 }
        }
    }),
    make_route_point_action({
        key = "lionheart_power_facility_1_complete_bridge_route_12959_-6252",
        label = "狮心_关闭供电设施_第一设施完成后补充路线_设施门口",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "完成"
        },
        constraint_mode = "all",
        trigger = {
            x = 12959.00,
            y = -6252.00,
            z = 2719.28,
            radius = 700,
            z_tolerance = 520
        },
        retry_ms = 600000,
        timeout_ms = 100000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 900,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        followup_route_action_key = "lionheart_power_facility_2_gather_13442_2735",
        waypoints = {
            { x = 11886.77, y = -6484.33, z = 2705.00 },
            { x = 10817.26, y = -6423.41, z = 2708.67 },
            { x = 10003.65, y = -6244.42, z = 2496.36 },
            { x = 9278.71, y = -6390.33, z = 2185.46 },
            { x = 8508.15, y = -6527.44, z = 2106.00 },
            { x = 7192.08, y = -6561.55, z = 2100.00 },
            { x = 4408.65, y = -4921.42, z = 2100.00 },
            { x = 5288.27, y = -1116.86, z = 2125.23 },
            { x = 5338.82, y = 2043.77, z = 2132.05 },
            { x = 7764.00, y = 2976.00, z = 2417.12 },
            { x = 9644.00, y = 3334.00, z = 2466.84 },
            { x = 10878.45, y = 2696.54, z = 2706.87 },
            { x = 13414.00, y = 2644.00, z = 2709.88 }
        }
    }),
    make_route_point_action({
        key = "lionheart_power_facility_2_gather_13442_2735",
        label = "狮心_关闭供电设施_第二设施MapTrapBtn_补充路线终点",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "关闭东北边的供电设施",
            "完成"
        },
        constraint_mode = "all",
        trigger = {
            x = 13441.90,
            y = 2734.86,
            z = 2712.25,
            radius = 760,
            z_tolerance = 520
        },
        interact_radius = 180,
        probe_retry_ms = 700,
        retry_ms = 600000,
        settle_ms = 2600,
        timeout_ms = 5200,
        force_task_call_after_transition = false,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "lionheart_power_facility_2_map_trap_btn_bridge",
            label = "第二供电设施交互按钮",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
            },
            hint_client_x = 704.528564,
            hint_client_y = 712.631531,
            hint_ratio_x = 0.489256,
            hint_ratio_y = 0.791813,
            hint_max_distance = 100.000,
            prefer_hint_fallback = true,
            hover_capture_enabled = true,
            hover_capture_client_left = 690.0,
            hover_capture_client_top = 685.0,
            hover_capture_client_right = 745.0,
            hover_capture_client_bottom = 730.0,
            hover_capture_retry_ms = 700,
            settle_ms = 1800,
            task_pos_reject_extra_ms = 3500
        }
    }),
    make_route_point_action({
        key = "lionheart_power_facility_2_route_13106_-5386",
        label = "狮心_关闭供电设施_第二设施录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "关闭东北边的供电设施",
            "完成"
        },
        constraint_mode = "all",
        trigger = {
            x = 12985.86,
            y = -6069.11,
            z = 2719.64,
            radius = 1300,
            z_tolerance = 700
        },
        retry_ms = 600000,
        timeout_ms = 80000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 900,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        waypoints = {
            { x = 13117.66, y = -5179.54, z = 2712.86 },
            { x = 13729.42, y = -3814.14, z = 2710.61 },
            { x = 13960.15, y = -2720.11, z = 2810.00 },
            { x = 13940.30, y = -1832.10, z = 2810.00 },
            { x = 13932.98, y = -940.49, z = 2810.52 },
            { x = 13926.47, y = -153.18, z = 2792.77 },
            { x = 13919.81, y = 600.28, z = 2714.25 },
            { x = 13909.78, y = 1381.68, z = 2705.00 },
            { x = 13838.53, y = 2093.45, z = 2705.00 }
        }
    }),
    make_route_point_action({
        key = "lionheart_power_facility_2_gather_13879_2079",
        label = "狮心_关闭供电设施_第二设施MapTrapBtn",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "关闭东北边的供电设施",
            "完成"
        },
        constraint_mode = "all",
        trigger = {
            x = 13878.81,
            y = 2079.32,
            z = 2705.00,
            radius = 760,
            z_tolerance = 520
        },
        interact_radius = 180,
        probe_retry_ms = 700,
        retry_ms = 600000,
        settle_ms = 2600,
        timeout_ms = 5200,
        force_task_call_after_transition = false,
        task_pos_reject_extra_ms = 3500,
        step = {
            key = "lionheart_power_facility_2_map_trap_btn",
            label = "第二供电设施交互按钮",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
            },
            hint_client_x = 704.528564,
            hint_client_y = 712.631531,
            hint_ratio_x = 0.489256,
            hint_ratio_y = 0.791813,
            hint_max_distance = 100.000,
            prefer_hint_fallback = true,
            hover_capture_enabled = true,
            hover_capture_client_left = 690.0,
            hover_capture_client_top = 685.0,
            hover_capture_client_right = 745.0,
            hover_capture_client_bottom = 730.0,
            hover_capture_retry_ms = 700,
            settle_ms = 1800,
            task_pos_reject_extra_ms = 3500
        }
    }),
    make_route_point_action({
        key = "eternal_sand_edge_land_route_-1983_4106",
        label = "永夜鸣沙_探索边陲之地_固定路径到Gather",
        mode = "recorded_route_point",
        task_patterns = {
            "永夜鸣沙"
        },
        task_detail_patterns = {
            "探索边陲之地",
            "沿途打听火种的下落"
        },
        constraint_mode = "all",
        complete_without_task_reacquire = true,
        followup_route_action_key = "eternal_sand_edge_land_gather_-2681_4179",
        trigger = {
            x = -1983.25,
            y = 4105.58,
            z = 32.00,
            radius = 700,
            z_tolerance = 220
        },
        retry_ms = 600000,
        timeout_ms = 90000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 220,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -1983.25, y = 4105.58, z = 32.00 },
            { x = -2342.64, y = 4697.89, z = 32.00 },
            { x = -2985.04, y = 4253.98, z = 32.00 },
            { x = -2681.46, y = 4179.09, z = 32.00 }
        }
    }),
    make_route_point_action({
        key = "eternal_sand_edge_land_gather_-2681_4179",
        label = "永夜鸣沙_探索边陲之地_终点Gather",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "永夜鸣沙"
        },
        task_detail_patterns = {
            "探索边陲之地",
            "沿途打听火种的下落"
        },
        constraint_mode = "all",
        trigger = {
            x = -2681.46,
            y = 4179.09,
            z = 32.00,
            radius = 520,
            z_tolerance = 220
        },
        interact_radius = 320,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 2200,
        timeout_ms = 18000,
        force_task_call_after_transition = false,
        step = {
            key = "eternal_sand_edge_land_gather_btn",
            label = "边陲之地GatherBtn",
            distance_anchor_exact_text = "阿瑞娅",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
            distance_min = 137.437046,
            distance_max = 142.437046,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn"
            },
            hint_client_x = 693.528564,
            hint_client_y = 699.631531,
            hint_ratio_x = 0.481617,
            hint_ratio_y = 0.777368,
            hint_max_distance = 80.000,
            prefer_hint_fallback = true,
            hover_capture_enabled = true,
            hover_capture_client_left = 680.0,
            hover_capture_client_top = 675.0,
            hover_capture_client_right = 750.0,
            hover_capture_client_bottom = 735.0,
            hover_capture_retry_ms = 700,
            settle_ms = 1800,
            task_pos_reject_extra_ms = 3500
        }
    }),
    make_route_point_action({
        key = "guiding_light_edge_land_gather_-3768_4006",
        label = "指路明灯_探索边陲之地_A到B_GatherBtn",
        mode = "objective_button_flow_point",
        task_patterns = {
            "指路明灯",
            "永夜鸣沙"
        },
        task_detail_patterns = {
            "探索边陲之地",
            "沿途打听火种的下落"
        },
        trigger = {
            x = -3768.00,
            y = 4006.00,
            z = 32.00,
            radius = 520,
            z_tolerance = 220
        },
        objective_point = {
            x = -2687.36,
            y = 4117.59,
            z = 32.00,
            radius = 160,
            z_tolerance = 220
        },
        require_destination_match = true,
        destination_match_radius = 1200,
        interact_radius = 160,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 2200,
        timeout_ms = 18000,
        force_task_call_after_transition = false,
        step = {
            key = "guiding_light_edge_land_gather_btn",
            label = "边陲之地GatherBtn",
            distance_anchor_exact_text = "阿瑞娅",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
            distance_min = 137.437046,
            distance_max = 142.437046,
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn"
            },
            hint_client_x = 693.528564,
            hint_client_y = 699.631531,
            hint_ratio_x = 0.481617,
            hint_ratio_y = 0.777368,
            hint_max_distance = 80.000
        }
    }),
    make_route_point_action({
        key = "guiding_light_repair_desert_lamp_route_-2337_3517",
        label = "指路明灯_维修沙漠之灯_录制路径后接Gather",
        mode = "recorded_route_point",
        task_patterns = {
            "指路明灯"
        },
        task_detail_patterns = {
            "维修沙漠之灯"
        },
        constraint_mode = "all",
        complete_without_task_reacquire = true,
        followup_route_action_key = "guiding_light_repair_desert_lamp_gather_-2744_4191",
        trigger = {
            x = -2336.90,
            y = 3516.98,
            z = 32.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 90000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -2336.90, y = 3516.98, z = 32.00 },
            { x = -3229.19, y = 4122.77, z = 32.00 },
            { x = -2743.97, y = 4191.09, z = 32.00 }
        }
    }),
    make_route_point_action({
        key = "guiding_light_repair_desert_lamp_gather_-2744_4191",
        label = "指路明灯_维修沙漠之灯_终点Gather",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        task_patterns = {
            "指路明灯"
        },
        task_detail_patterns = {
            "维修沙漠之灯"
        },
        constraint_mode = "all",
        trigger = {
            x = -2743.97,
            y = 4191.09,
            z = 32.00,
            radius = 520,
            z_tolerance = 260
        },
        interact_radius = 320,
        pre_action_combat_guard = false,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 1600,
        timeout_ms = 14000,
        step = {
            key = "guiding_light_repair_desert_lamp_gather_btn",
            label = "维修沙漠之灯Gather按钮",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn"
            },
            hint_client_x = 698.053040,
            hint_client_y = 706.797729,
            hint_ratio_x = 0.485096,
            hint_ratio_y = 0.785331,
            hint_max_distance = 90.000,
            prefer_hint_fallback = true,
            settle_ms = 1200
        }
    }),
    make_route_point_action({
        key = "yellow_sand_sunken_dunes_route_7206_3137",
        label = "黄沙迢迢_探索沉没沙丘_录制路径后重call主线",
        mode = "recorded_route_point",
        task_patterns = {
            "黄沙迢迢"
        },
        task_detail_patterns = {
            "探索沉没沙丘，打听伊吉部族和火种的下落"
        },
        constraint_mode = "all",
        require_destination_match = true,
        destination_match_radius = 1800,
        trigger = {
            x = 7206.00,
            y = 3137.00,
            z = 2491.00,
            radius = 1500,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 210000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 8048.30, y = 3604.33, z = 2491.00 },
            { x = 8685.24, y = 3114.89, z = 2491.00 },
            { x = 9474.63, y = 2369.36, z = 2491.00 },
            { x = 9922.41, y = 1736.90, z = 2491.00 },
            { x = 10491.38, y = 743.37, z = 2491.00 },
            { x = 10905.17, y = 17.13, z = 2491.00 },
            { x = 11378.81, y = -601.05, z = 2491.00 },
            { x = 12284.64, y = -1470.26, z = 2491.00 },
            { x = 12992.25, y = -1806.04, z = 2491.00 },
            { x = 13626.59, y = -2152.43, z = 2491.00 },
            { x = 14270.57, y = -2595.87, z = 2491.00 },
            { x = 14888.51, y = -3027.15, z = 2491.00 },
            { x = 15540.90, y = -3346.94, z = 2491.00 },
            { x = 16145.75, y = -3732.92, z = 2491.00 },
            { x = 16595.14, y = -4266.89, z = 2491.00 },
            { x = 16792.00, y = -5341.00, z = 2491.00 },
            { x = 16910.00, y = -5549.00, z = 2491.00 }
        }
    }),
    make_npc_dialogue_route_action({
        key = "long_night_star_thank_yiji_girl_dialogue_16895_-6254",
        label = "长夜明星_感谢伊吉女孩的相助_NPC对话",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        retry_ms = 600000,
        task_patterns = {
            "长夜明星"
        },
        task_detail_patterns = {
            "感谢伊吉女孩的相助"
        },
        constraint_mode = "all",
        trigger = {
            x = 16895.03,
            y = -6253.81,
            z = 2491.00,
            radius = 1200,
            z_tolerance = 320
        },
        dialogue = {
            x = 16895.03,
            y = -6253.81,
            z = 2491.00,
            radius = 360,
            interact_radius = 180,
            move_interval_ms = 220,
            z_tolerance = 320,
            center_settle_ms = 600,
            interact_retry_ms = 1400,
            timeout_ms = 22000,
            npc_search_radius = 900,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "long_night_star_go_to_falling_star_market_no_path_route_17034_-7147",
        label = "长夜明星_与艾丝一同前往坠星集市_无路径固定路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        allow_during_task_button_refresh = true,
        wait_task_path_recover_only = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "长夜明星"
        },
        task_detail_patterns = {
            "与艾丝一同前往坠星集市"
        },
        constraint_mode = "all",
        trigger = {
            x = 17034.69,
            y = -7147.79,
            z = 2491.00,
            radius = 1600,
            z_tolerance = 420
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 240,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        route_worker_max_points = 4,
        waypoints = {
            { x = 17034.69, y = -7147.79, z = 2491.00 },
            { x = 17240.31, y = -7782.70, z = 2491.00 },
            { x = 17493.57, y = -8233.29, z = 2491.00 },
            { x = 18637.48, y = -8377.18, z = 2495.53 }
        }
    }),
    make_route_point_action({
        key = "sand_sea_find_iji_fire_seed_route_8639_10964",
        label = "狂沙地海_找到伊吉部族追寻火种_固定路线后重call主线",
        mode = "recorded_route_point",
        task_patterns = {
            "狂沙地海"
        },
        task_detail_patterns = {
            "找到伊吉部族，追寻火种的下落"
        },
        constraint_mode = "all",
        trigger = {
            x = 8639.00,
            y = 10964.00,
            z = 16.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 300000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 9960.06, y = 9900.01, z = 16.00 },
            { x = 10589.10, y = 9544.71, z = 16.00 },
            { x = 11114.22, y = 9457.03, z = 16.00 },
            { x = 11729.12, y = 9351.61, z = 16.00 },
            { x = 12289.04, y = 9286.53, z = 16.00 },
            { x = 12820.16, y = 9156.35, z = 16.00 },
            { x = 13209.96, y = 8946.44, z = 16.00 },
            { x = 13572.89, y = 8677.25, z = 16.00 },
            { x = 13862.28, y = 8320.23, z = 16.00 },
            { x = 13996.08, y = 8009.69, z = 16.00 },
            { x = 13691.73, y = 8359.18, z = 16.00 },
            { x = 13356.99, y = 8699.21, z = 16.00 },
            { x = 13008.81, y = 8931.03, z = 16.00 },
            { x = 12612.87, y = 9087.23, z = 16.00 },
            { x = 12141.79, y = 9125.95, z = 16.00 },
            { x = 11779.88, y = 9035.66, z = 16.00 },
            { x = 11390.12, y = 8817.44, z = 16.00 },
            { x = 11100.48, y = 8491.29, z = 16.00 },
            { x = 10885.94, y = 8107.47, z = 16.00 },
            { x = 10745.57, y = 7724.20, z = 16.00 },
            { x = 10701.00, y = 7356.44, z = 16.00 },
            { x = 10785.13, y = 6918.34, z = 16.00 },
            { x = 10927.73, y = 6596.99, z = 16.00 },
            { x = 11130.12, y = 6306.59, z = 16.00 },
            { x = 11504.20, y = 6003.63, z = 16.00 },
            { x = 11819.20, y = 5767.50, z = 16.00 },
            { x = 12168.43, y = 5539.80, z = 16.00 },
            { x = 12551.21, y = 5378.30, z = 16.00 },
            { x = 12910.34, y = 5356.61, z = 16.00 },
            { x = 13273.42, y = 5507.64, z = 16.00 },
            { x = 13586.27, y = 5715.05, z = 16.00 },
            { x = 13879.31, y = 5978.15, z = 16.00 },
            { x = 14070.56, y = 6217.92, z = 16.00 },
            { x = 14256.60, y = 6660.75, z = 16.00 },
            { x = 14366.12, y = 7044.51, z = 16.00 },
            { x = 14559.32, y = 6902.52, z = 16.00 },
            { x = 14386.79, y = 6478.98, z = 16.00 },
            { x = 14079.17, y = 6078.40, z = 16.00 },
            { x = 13709.70, y = 5763.94, z = 16.00 },
            { x = 13404.85, y = 5587.17, z = 16.00 },
            { x = 13447.09, y = 5919.65, z = 16.00 },
            { x = 13734.30, y = 6222.14, z = 16.00 },
            { x = 14021.79, y = 6523.82, z = 16.00 },
            { x = 14277.79, y = 6792.59, z = 16.00 },
            { x = 14469.42, y = 7082.99, z = 16.00 },
            { x = 14449.27, y = 7378.92, z = 16.00 },
            { x = 14378.38, y = 7126.75, z = 16.00 },
            { x = 14294.51, y = 6730.63, z = 16.00 },
            { x = 14138.28, y = 6391.60, z = 16.00 },
            { x = 13917.24, y = 6090.29, z = 16.00 },
            { x = 13665.19, y = 5878.91, z = 16.00 },
            { x = 13388.02, y = 5660.05, z = 16.00 },
            { x = 13098.80, y = 5460.39, z = 16.00 },
            { x = 12827.73, y = 5323.72, z = 16.00 },
            { x = 12486.53, y = 5251.21, z = 16.00 },
            { x = 12124.99, y = 5290.49, z = 16.00 },
            { x = 11822.70, y = 5440.45, z = 16.00 },
            { x = 11557.93, y = 5668.62, z = 16.00 },
            { x = 11369.30, y = 5926.79, z = 16.00 },
            { x = 11200.61, y = 6230.54, z = 16.00 },
            { x = 11061.23, y = 6576.45, z = 16.00 },
            { x = 10949.99, y = 6901.05, z = 16.00 },
            { x = 10887.77, y = 7350.03, z = 16.00 },
            { x = 10897.37, y = 7702.25, z = 16.00 },
            { x = 10984.08, y = 8006.62, z = 16.00 },
            { x = 11116.87, y = 8305.91, z = 16.00 },
            { x = 11291.97, y = 8516.37, z = 16.00 },
            { x = 11521.58, y = 8720.42, z = 16.00 },
            { x = 11791.86, y = 8865.57, z = 16.00 },
            { x = 12075.34, y = 8950.69, z = 16.00 },
            { x = 12359.36, y = 8997.18, z = 16.00 },
            { x = 12698.77, y = 9020.04, z = 16.00 },
            { x = 13026.16, y = 9037.57, z = 16.00 },
            { x = 13353.08, y = 8993.41, z = 16.00 },
            { x = 13132.31, y = 9083.48, z = 16.00 },
            { x = 12841.08, y = 9118.38, z = 16.00 },
            { x = 12537.50, y = 9086.02, z = 16.00 },
            { x = 12244.92, y = 9026.75, z = 16.00 },
            { x = 11927.02, y = 8940.41, z = 16.00 },
            { x = 11652.34, y = 8806.28, z = 16.00 },
            { x = 11318.27, y = 8578.70, z = 16.00 },
            { x = 11042.93, y = 8323.65, z = 16.00 },
            { x = 10853.66, y = 8097.91, z = 16.00 },
            { x = 10727.16, y = 7869.04, z = 16.00 },
            { x = 10616.05, y = 7467.48, z = 16.00 }
        }
    }),
    make_route_point_action({
        key = "wind_night_follow_iji_trace_highfreq_route_-5794_-4616",
        label = "永夜之风_跟随伊吉人的踪迹_高频固定路线后重call主线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "永夜之风"
        },
        task_detail_patterns = {
            "跟随伊吉人的踪迹，找到伊吉部族"
        },
        constraint_mode = "all",
        trigger = {
            x = -5794.00,
            y = -4616.00,
            z = 1610.00,
            radius = 900,
            z_tolerance = 420
        },
        retry_ms = 600000,
        timeout_ms = 120000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 420,
        move_interval_ms = 30,
        move_interval_floor_ms = 30,
        reacquire_retry_ms = 800,
        route_stuck_skip_ms = 30000,
        waypoints = {
            { x = -1367.66, y = -3464.22, z = 1603.00, move_interval_ms = 30, move_interval_floor_ms = 30 },
            { x = -1450.49, y = -1325.49, z = 1606.00, move_interval_ms = 30, move_interval_floor_ms = 30 },
            { x = 343.48, y = 261.42, z = 1603.00, move_interval_ms = 30, move_interval_floor_ms = 30 },
            { x = 785.02, y = -402.19, z = 1605.31, move_interval_ms = 30, move_interval_floor_ms = 30 },
            { x = 3578.59, y = -1141.58, z = 1613.66, move_interval_ms = 30, move_interval_floor_ms = 30 }
        }
    }),
    make_route_point_action({
        key = "sky_rift_giant_cannon_route_loop_2368_-8862",
        label = "天堑歧路_寻找通往巨炮的道路_固定路线直到任务刷新",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "sky_rift_giant_cannon_route_loop_2368_-8862",
        task_patterns = {
            "天堑歧路"
        },
        task_detail_patterns = {
            "寻找通往巨炮的道路"
        },
        constraint_mode = "all",
        trigger = {
            x = 2367.70,
            y = -8862.28,
            z = 86.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 240000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 1756.83, y = -8787.88, z = 86.00 },
            { x = 1219.40, y = -8666.88, z = 86.00 },
            { x = 868.64, y = -8408.96, z = 86.00 },
            { x = 678.52, y = -8004.62, z = 86.00 },
            { x = 647.04, y = -7636.01, z = 86.00 },
            { x = 734.55, y = -7260.59, z = 86.00 },
            { x = 991.57, y = -6967.92, z = 86.00 },
            { x = 1344.68, y = -6818.18, z = 86.00 },
            { x = 1664.28, y = -6828.89, z = 86.00 },
            { x = 1967.39, y = -7003.92, z = 86.00 },
            { x = 2095.97, y = -7348.97, z = 86.00 },
            { x = 2113.45, y = -7716.38, z = 86.00 },
            { x = 2121.63, y = -8062.55, z = 86.00 },
            { x = 1991.28, y = -8380.56, z = 86.00 },
            { x = 1718.94, y = -8614.19, z = 86.00 },
            { x = 1384.81, y = -8696.11, z = 86.00 },
            { x = 1071.07, y = -8662.04, z = 86.00 },
            { x = 845.31, y = -8402.38, z = 86.00 },
            { x = 711.37, y = -8062.62, z = 86.00 },
            { x = 700.50, y = -7716.18, z = 86.00 },
            { x = 786.88, y = -7324.77, z = 86.00 },
            { x = 958.17, y = -6927.34, z = 86.00 },
            { x = 1223.29, y = -6734.72, z = 86.00 },
            { x = 1567.87, y = -6760.54, z = 86.00 },
            { x = 1766.07, y = -7043.52, z = 86.00 },
            { x = 1968.30, y = -7388.63, z = 86.00 },
            { x = 2102.86, y = -7626.49, z = 86.00 },
            { x = 2160.69, y = -7989.35, z = 86.00 },
            { x = 2108.72, y = -8346.73, z = 86.00 },
            { x = 1861.08, y = -8583.51, z = 86.00 },
            { x = 1584.07, y = -8692.05, z = 86.00 }
        }
    }),
    make_route_point_action({
        key = "sky_rift_cross_wall_cannon_route_loop_8782_10765",
        label = "天堑歧路_穿越巨墙到摧毁驻墙炮_跑打路线直到任务完成",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "sky_rift_cross_wall_cannon_route_loop_8782_10765",
        task_patterns = {
            "天堑歧路"
        },
        task_detail_patterns = {
            "继续前进，穿越巨墙",
            "摧毁驻墙炮",
            "摧毁筑墙炮"
        },
        constraint_mode = "all",
        trigger = {
            x = 8782.00,
            y = 10765.00,
            z = 86.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 240000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 8641.79, y = 11673.27, z = 86.00 },
            { x = 9496.08, y = 13424.81, z = 86.00 },
            { x = 9172.51, y = 13972.56, z = 86.00 },
            { x = 8606.10, y = 13918.16, z = 86.00 },
            { x = 7998.49, y = 13594.15, z = 86.00 },
            { x = 7549.61, y = 13120.57, z = 86.00 },
            { x = 7384.69, y = 12522.47, z = 86.00 },
            { x = 7554.50, y = 11975.18, z = 86.00 },
            { x = 8158.14, y = 11885.33, z = 86.00 },
            { x = 8721.16, y = 11985.37, z = 86.00 }
        }
    }),
    make_route_point_action({
        key = "wall_of_sighs_manual_route_5104_-1737",
        label = "\u{53F9}\u{606F}\u{4E4B}\u{5899}\u{5F}\u{7A7F}\u{8D8A}\u{610F}\u{5FD7}\u{9AD8}\u{5899}\u{5F}\u{5F55}\u{5236}\u{8DEF}\u{5F84}\u{7EA0}\u{504F}",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
        },
        task_detail_patterns = {
            "\u{7A7F}\u{8D8A}\u{610F}\u{5FD7}\u{9AD8}\u{5899}"
        },
        constraint_mode = "all",
        trigger = {
            x = 5103.69,
            y = -1737.02,
            z = 83.00,
            radius = 620,
            z_tolerance = 260
        },
        retry_ms = 15000,
        timeout_ms = 120000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 4562.00, y = -1632.00, z = 83.00 },
            { x = 3852.37, y = -1422.76, z = 83.00 },
            { x = 3215.77, y = -1427.42, z = 83.00 },
            { x = 2682.92, y = -1733.01, z = 83.00 },
            { x = 2136.58, y = -1803.37, z = 83.00 },
            { x = 1586.25, y = -1621.91, z = 83.00 },
            { x = 1000.04, y = -1434.84, z = 83.00 },
            { x = 1210.01, y = -1523.69, z = 83.00 },
            { x = 1782.58, y = -1730.36, z = 83.00 },
            { x = 2213.47, y = -1884.98, z = 83.00 },
            { x = 2659.34, y = -1781.31, z = 83.00 },
            { x = 3084.00, y = -1598.84, z = 83.00 },
            { x = 3482.57, y = -1427.95, z = 83.00 },
            { x = 3964.45, y = -1347.75, z = 83.00 },
            { x = 4466.85, y = -1469.11, z = 83.00 },
            { x = 4769.89, y = -1655.71, z = 83.00 },
            { x = 4386.54, y = -1628.98, z = 83.00 },
            { x = 3953.69, y = -1457.27, z = 83.00 },
            { x = 3566.22, y = -1398.79, z = 83.00 },
            { x = 3201.30, y = -1584.35, z = 83.00 },
            { x = 2802.98, y = -1782.33, z = 83.00 },
            { x = 2431.57, y = -1864.69, z = 83.00 },
            { x = 1994.73, y = -1832.29, z = 83.00 },
            { x = 1522.76, y = -1703.82, z = 83.00 },
            { x = 1126.30, y = -1570.39, z = 83.00 },
            { x = 1061.03, y = -1540.58, z = 83.00 },
            { x = 1479.21, y = -1659.69, z = 83.00 },
            { x = 1868.39, y = -1770.31, z = 83.00 },
            { x = 2296.70, y = -1842.71, z = 83.00 },
            { x = 2693.32, y = -1753.30, z = 83.00 },
            { x = 3040.39, y = -1631.00, z = 83.00 },
            { x = 3375.20, y = -1521.82, z = 83.00 },
            { x = 3747.43, y = -1445.38, z = 83.00 },
            { x = 4140.29, y = -1527.05, z = 83.00 },
            { x = 4339.51, y = -1616.14, z = 83.00 },
            { x = 3974.86, y = -1434.32, z = 83.00 },
            { x = 3633.23, y = -1383.65, z = 83.00 },
            { x = 3298.84, y = -1498.25, z = 83.00 },
            { x = 2945.67, y = -1634.29, z = 83.00 },
            { x = 2610.07, y = -1741.64, z = 83.00 },
            { x = 2257.78, y = -1781.44, z = 83.00 },
            { x = 1884.37, y = -1729.73, z = 83.00 },
            { x = 1511.91, y = -1650.81, z = 83.00 },
            { x = 1171.05, y = -1567.77, z = 83.00 },
            { x = 825.95, y = -1480.59, z = 83.00 },
            { x = 528.97, y = -1491.74, z = 83.00 },
            { x = 334.60, y = -1387.22, z = 83.00 },
            { x = 491.23, y = -1172.28, z = 83.00 },
            { x = 717.24, y = -1089.14, z = 83.00 },
            { x = 957.88, y = -1127.55, z = 83.00 },
            { x = 1120.03, y = -1310.11, z = 83.00 },
            { x = 1125.82, y = -1527.28, z = 83.00 },
            { x = 1043.60, y = -1722.97, z = 83.00 },
            { x = 848.92, y = -1806.20, z = 83.00 },
            { x = 659.96, y = -1779.38, z = 83.00 },
            { x = 505.13, y = -1639.26, z = 83.00 },
            { x = 662.69, y = -1560.71, z = 83.00 },
            { x = 868.51, y = -1576.63, z = 83.00 },
            { x = 1056.72, y = -1605.86, z = 83.00 },
            { x = 1270.49, y = -1638.57, z = 83.00 },
            { x = 1460.19, y = -1666.89, z = 83.00 },
            { x = 1648.97, y = -1695.47, z = 83.00 },
            { x = 1890.19, y = -1731.33, z = 83.00 },
            { x = 2105.93, y = -1759.59, z = 83.00 },
            { x = 2320.00, y = -1768.80, z = 83.00 },
            { x = 2537.54, y = -1737.88, z = 83.00 },
            { x = 2723.78, y = -1696.68, z = 83.00 },
            { x = 2909.61, y = -1653.73, z = 83.00 },
            { x = 3122.64, y = -1605.39, z = 83.00 },
            { x = 3309.43, y = -1576.46, z = 83.00 },
            { x = 3509.96, y = -1563.57, z = 83.00 },
            { x = 3702.82, y = -1569.92, z = 83.00 },
            { x = 3891.44, y = -1589.71, z = 83.00 },
            { x = 4079.56, y = -1611.45, z = 83.00 },
            { x = 4395.71, y = -1623.70, z = 83.00 },
            { x = 4174.17, y = -1508.64, z = 83.00 },
            { x = 2695.66, y = -1618.05, z = 83.00 },
            { x = 1923.40, y = -1795.60, z = 83.00 },
            { x = 914.42, y = -1287.65, z = 83.00 },
            { x = 569.19, y = -210.20, z = 120.09 },
            { x = 951.67, y = 667.74, z = 291.00 },
            { x = 1828.45, y = 1065.07, z = 155.19 },
            { x = 3068.67, y = 1151.51, z = 83.00 },
            { x = 4166.19, y = 881.15, z = 83.00 },
            { x = 5518.08, y = 736.25, z = 83.00 },
            { x = 5761.54, y = -121.20, z = 83.00 },
            { x = 6258.07, y = -1868.18, z = 83.00 },
            { x = 5728.66, y = -2907.75, z = 83.00 }
        }
    }),
    make_route_point_action({
        key = "wall_of_sighs_forbidden_wall_no_path_route_-16603_9004",
        label = "叹息之墙_前往禁忌高墙_无路径移动到过图门",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        wait_task_path_recover_only = true,
        drop_active_when_task_mismatch = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "wall_of_sighs_forbidden_wall_no_path_portal_-16603_9004",
        followup_route_action_ignore_retry = true,
        task_patterns = {
            "叹息之墙"
        },
        task_detail_patterns = {
            "前往禁忌高墙"
        },
        constraint_mode = "all",
        trigger = {
            x = -16603.00,
            y = 9004.00,
            z = 1004.00,
            radius = 1800,
            z_tolerance = 420
        },
        retry_ms = 600000,
        timeout_ms = 45000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        route_worker_max_points = 3,
        waypoints = {
            { x = -16603.00, y = 9004.00, z = 1004.00 }
        }
    }),
    make_route_point_action({
        key = "wall_of_sighs_forbidden_wall_no_path_portal_-16603_9004",
        label = "叹息之墙_前往禁忌高墙_无路径过图门",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        wait_task_path_recover_only = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "叹息之墙"
        },
        task_detail_patterns = {
            "前往禁忌高墙"
        },
        constraint_mode = "all",
        trigger = {
            x = -16603.00,
            y = 9004.00,
            z = 1004.00,
            radius = 900,
            z_tolerance = 420
        },
        objective_point = {
            x = -16603.00,
            y = 9004.00,
            z = 1004.00,
            radius = 260,
            z_tolerance = 420
        },
        interact_radius = 260,
        probe_retry_ms = 700,
        retry_ms = 2500,
        settle_ms = 4500,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 3500,
        fallback_interact = true,
        fallback_interact_distance = 320,
        fallback_retry_ms = 2500,
        step = {
            key = "wall_of_sighs_forbidden_wall_no_path_portal_btn_-16603_9004",
            label = "禁忌高墙过图门PortalBtn",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
            },
            hint_client_x = 697.204834,
            hint_client_y = 724.439941,
            hint_ratio_x = 0.484170,
            hint_ratio_y = 0.804933,
            hint_max_distance = 180.000
        }
    }),
    make_route_point_action({
        key = "wall_of_sighs_forbidden_wall_end_route_31064_21852",
        label = "叹息之墙_前往禁忌高墙尽头_固定路线后重call主线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        drop_active_when_task_mismatch = true,
        task_patterns = {
            "叹息之墙"
        },
        task_detail_patterns = {
            "前往禁忌高墙尽头"
        },
        constraint_mode = "all",
        trigger = {
            x = 31064.00,
            y = 21852.00,
            z = 5363.75,
            radius = 700,
            z_tolerance = 520
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 520,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 31652.79, y = 21901.38, z = 5516.00 },
            { x = 32026.75, y = 22163.70, z = 5549.58 },
            { x = 32072.37, y = 22601.09, z = 5652.78 },
            { x = 31971.28, y = 22928.11, z = 5686.81 },
            { x = 31805.03, y = 23143.97, z = 5693.00 },
            { x = 31632.04, y = 23423.87, z = 5693.00 },
            { x = 31722.75, y = 23698.59, z = 5693.00 },
            { x = 31979.46, y = 23710.03, z = 5693.00 },
            { x = 32028.68, y = 23506.60, z = 5693.00 },
            { x = 32003.71, y = 23286.45, z = 5693.00 },
            { x = 31978.22, y = 23077.54, z = 5693.00 },
            { x = 31954.06, y = 22890.86, z = 5683.23 },
            { x = 31926.17, y = 22674.86, z = 5658.97 },
            { x = 31895.30, y = 22460.20, z = 5613.33 },
            { x = 31871.87, y = 22309.21, z = 5571.12 },
            { x = 31843.88, y = 22129.32, z = 5524.04 },
            { x = 31822.83, y = 21993.90, z = 5515.00 },
            { x = 31799.69, y = 21820.75, z = 5513.95 },
            { x = 31796.65, y = 21737.95, z = 5513.16 },
            { x = 31920.49, y = 21845.70, z = 5513.00 },
            { x = 31993.96, y = 22040.21, z = 5521.05 },
            { x = 31983.06, y = 22237.40, z = 5568.23 },
            { x = 31966.52, y = 22472.36, z = 5622.47 },
            { x = 31956.84, y = 22612.31, z = 5649.42 },
            { x = 31948.07, y = 22739.46, z = 5665.68 },
            { x = 31939.50, y = 22858.30, z = 5680.07 },
            { x = 31930.73, y = 22972.26, z = 5692.77 },
            { x = 31924.50, y = 23096.95, z = 5693.00 },
            { x = 31925.64, y = 23150.81, z = 5693.00 },
            { x = 31933.37, y = 23123.67, z = 5693.00 },
            { x = 31954.58, y = 23017.08, z = 5693.00 },
            { x = 31970.68, y = 22914.73, z = 5685.17 },
            { x = 31973.28, y = 22807.69, z = 5671.76 },
            { x = 31964.34, y = 22656.61, z = 5654.30 },
            { x = 31955.43, y = 22505.87, z = 5631.99 },
            { x = 31955.68, y = 22353.66, z = 5592.44 },
            { x = 31983.93, y = 22193.22, z = 5557.29 },
            { x = 32010.25, y = 22065.31, z = 5526.91 },
            { x = 32026.65, y = 21924.25, z = 5513.00 },
            { x = 32027.47, y = 21917.18, z = 5513.00 },
            { x = 32035.04, y = 22023.80, z = 5516.58 },
            { x = 32044.06, y = 22152.29, z = 5544.94 },
            { x = 32056.40, y = 22303.63, z = 5580.12 },
            { x = 32066.68, y = 22432.67, z = 5609.84 },
            { x = 32073.82, y = 22586.24, z = 5651.20 },
            { x = 32073.79, y = 22726.70, z = 5668.06 },
            { x = 32064.80, y = 22857.97, z = 5683.17 },
            { x = 32048.21, y = 23012.46, z = 5693.00 },
            { x = 32028.06, y = 23141.26, z = 5693.00 },
            { x = 32016.32, y = 23210.50, z = 5693.00 },
            { x = 32030.15, y = 23126.68, z = 5693.00 },
            { x = 32044.27, y = 22972.11, z = 5692.57 },
            { x = 32030.27, y = 22833.91, z = 5676.30 },
            { x = 31999.63, y = 22707.71, z = 5658.87 },
            { x = 31956.82, y = 22558.48, z = 5643.41 },
            { x = 31917.81, y = 22413.82, z = 5602.40 },
            { x = 31901.94, y = 22260.13, z = 5562.96 },
            { x = 31897.26, y = 22083.62, z = 5520.06 },
            { x = 31872.14, y = 21887.91, z = 5513.99 },
            { x = 29862.31, y = 22031.38, z = 5328.00 },
            { x = 29561.44, y = 21710.97, z = 5328.00 },
            { x = 29004.44, y = 21465.12, z = 5333.00 },
            { x = 28935.18, y = 20812.87, z = 5410.40 }
        }
    }),
    make_route_point_action({
        key = "wall_of_sighs_will_wall_anchor_-5776_2400",
        label = "\u{53F9}\u{606F}\u{4E4B}\u{5899}_\u{7A7F}\u{8D8A}\u{610F}\u{5FD7}\u{9AD8}\u{5899}_\u{5165}\u{53E3}\u{951A}\u{70B9}",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        task_patterns = {
            "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
        },
        task_detail_patterns = {
            "\u{7A7F}\u{8D8A}\u{610F}\u{5FD7}\u{9AD8}\u{5899}"
        },
        map_patterns = {
            "\u{610F}\u{5FD7}\u{9AD8}\u{5899}"
        },
        constraint_mode = "all",
        trigger = {
            x = -5776.00,
            y = 2400.00,
            z = 523.00,
            radius = 1900,
            z_tolerance = 220
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 220,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -5776.00, y = 2400.00, z = 523.00 }
        }
    }),
    make_route_point_action({
        key = "eternal_night_crossing_route_-4588_-4023",
        label = "\u{6C38}\u{591C}\u{4E4B}\u{98CE}_\u{7A7F}\u{8D8A}\u{6C38}\u{591C}\u{4E4B}\u{98CE}_\u{5F55}\u{5236}\u{8DEF}\u{7EBF}",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{6C38}\u{591C}\u{4E4B}\u{98CE}"
        },
        task_detail_patterns = {
            "\u{7A7F}\u{8D8A}\u{6C38}\u{591C}\u{4E4B}\u{98CE}"
        },
        constraint_mode = "all",
        trigger = {
            x = -5191.00,
            y = -4262.00,
            z = 1605.72,
            radius = 1500,
            z_tolerance = 320
        },
        retry_ms = 600000,
        timeout_ms = 240000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -5191.00, y = -4262.00, z = 1605.72 },
            { x = -4240.00, y = -4000.00, z = 1603.00 },
            { x = -3869.79, y = -3868.36, z = 1603.00 },
            { x = -3407.35, y = -3767.77, z = 1603.00 },
            { x = -3029.44, y = -3418.71, z = 1603.00 },
            { x = -2959.84, y = -3271.93, z = 1603.00 },
            { x = -2923.10, y = -2739.76, z = 1603.00 },
            { x = -2953.16, y = -2253.22, z = 1603.00 },
            { x = -2614.72, y = -1618.82, z = 1603.00 },
            { x = -2192.00, y = -1056.00, z = 1613.00 },
            { x = -1097.89, y = -607.74, z = 1603.00 },
            { x = -643.52, y = -349.40, z = 1603.00 },
            { x = -218.51, y = -21.50, z = 1603.00 },
            { x = 372.09, y = 263.45, z = 1603.00 },
            { x = 565.05, y = 18.20, z = 1603.00 },
            { x = 685.78, y = -78.91, z = 1604.06 },
            { x = 969.56, y = -225.86, z = 1628.37 },
            { x = 1446.02, y = -201.45, z = 1632.00 },
            { x = 1816.27, y = -151.50, z = 1583.44 },
            { x = 2122.52, y = -303.54, z = 1604.00 },
            { x = 2421.11, y = -514.51, z = 1607.70 },
            { x = 2458.97, y = -506.27, z = 1610.44 },
            { x = 2620.74, y = -640.15, z = 1623.69 },
            { x = 2802.81, y = -768.79, z = 1637.15 },
            { x = 2974.85, y = -855.50, z = 1637.53 },
            { x = 3176.62, y = -950.85, z = 1631.30 },
            { x = 3358.73, y = -1003.97, z = 1610.40 },
            { x = 3580.60, y = -1004.33, z = 1607.43 },
            { x = 3803.41, y = -1004.67, z = 1604.19 },
            { x = 4154.82, y = -977.11, z = 1603.00 }
        }
    })
}

M.FORCE_KITE_MONSTER_NAMES = Actions.make_force_kite_name_set({
    "\u{88AB}\u{64CD}\u{7EB5}\u{7684}\u{54E5}\u{5E03}\u{6797}\u{5F13}\u{7BAD}\u{624B}",
    "\u{89C9}\u{9192}\u{60E9}\u{7F5A}\u{8005}",
    "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}",
    "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{5F02}\u{517D}",
    "\u{9AD8}\u{539F}\u{957F}\u{89D2}\u{5F02}\u{517D}",
    "\u{5DE8}\u{578B}\u{8815}\u{866B}"
})

M.OBJECTIVE_POINT_CONFIGS = {
    Actions.make_objective_point({
        key = "controlled_goblin_boss_room",
        x = 12351.00,
        y = -7100.00,
        z = 777.00,
        radius = 520,
        trigger_distance = 520,
        immediate_kite_on_reached = true,
        kite_radius = 1260,
        kite_point_count = 3,
        kite_switch_ms = 2400,
        seamless_kite = true,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        task_patterns = {
            "\u{51FB}\u{8D25}\u{88AB}\u{64CD}\u{7EB5}\u{7684}\u{54E5}\u{5E03}\u{6797}",
            "\u{88AB}\u{64CD}\u{7EB5}\u{7684}\u{54E5}\u{5E03}\u{6797}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        },
        exclude_task_detail_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    }),
    Actions.make_clear_room_point({
        key = "kingdom_end_deep_boss_kite",
        x = 7650.00,
        y = -2315.00,
        z = -3106.00,
        radius = 1600,
        trigger_distance = 1000,
        immediate_kite_on_reached = true,
        kite_radius = 2400,
        kite_switch_ms = 2200,
        seamless_kite = true,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        generic_followup_refresh_ms = 3500,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        kite_points = {
            { x = 7221.80, y = -2788.37, z = -3106.00 },
            { x = 7256.24, y = -2019.35, z = -3106.00 },
            { x = 8473.43, y = -2139.18, z = -3105.92 }
        },
        task_patterns = {
            "\u{738B}\u{56FD}\u{7EC8}\u{9014}",
            "\u{4E3B}\u{7EBF} \u{738B}\u{56FD}\u{7EC8}\u{9014}",
            "\u{524D}\u{5F80}\u{56FD}\u{738B}\u{752C}\u{9053}",
            "\u{8FDB}\u{5165}\u{56FD}\u{738B}\u{752C}\u{9053}",
            "\u{51B2}\u{8FC7}\u{91CD}\u{56F4}\u{FF0C}\u{7A7F}\u{8D8A}\u{56FD}\u{738B}\u{752C}\u{9053}",
            "\u{62B5}\u{8FBE}\u{56FD}\u{738B}\u{752C}\u{9053}\u{6700}\u{6DF1}\u{5904}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}",
            "\u{85CF}\u{5B9D}\u{5730}"
        },
        exclude_task_detail_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}",
            "\u{85CF}\u{5B9D}\u{5730}"
        }
    }),
    Actions.make_clear_room_point({
        key = "mountain_heart_clear_room_2473_11658",
        x = 2472.71,
        y = 11657.83,
        z = -417.00,
        radius = 980,
        trigger_distance = 720,
        kite_radius = 2880,
        boss_clear_settle_ms = 3000,
        post_combat_loot = false,
        task_patterns = {
            "\u{7FA4}\u{5C71}\u{4E4B}\u{5FC3}",
            "\u{5B8C}\u{6210}"
        },
        task_detail_patterns = {
            "阻止矮人王的阴谋"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    }),
    Actions.make_clear_room_point({
        key = "mountain_heart_dwarf_king_endpoint_kite",
        x = 7451.00,
        y = 17193.00,
        z = 1010.00,
        radius = 900,
        trigger_distance = 520,
        immediate_kite_on_reached = true,
        allow_no_task_target_force_kite = true,
        kite_radius = 1260,
        kite_point_count = 3,
        kite_switch_ms = 2200,
        seamless_kite = true,
        kite_arrive_distance = 420,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        generic_followup_refresh_ms = 3500,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        ignore_terminal_text_change_when_objective_same = true,
        post_combat_loot = {
            enabled = true,
            duration_ms = 3500,
            max_duration_ms = 7000,
            press_interval_ms = 450,
            empty_settle_ms = 900
        },
        revive_reentry = make_mountain_heart_dwarf_king_reentry_config(),
        task_patterns = {
            "群山之心"
        },
        task_detail_patterns = {
            "击败矮人王多加尔"
        },
        exclude_task_detail_patterns = {
            "交谈",
            "对话"
        }
    }),
    Actions.make_clear_room_point({
        key = "counterattack_dawn_worm_room_17190_16840",
        x = 19290.00,
        y = 20629.00,
        z = 920.00,
        radius = 1800,
        trigger_distance = 1400,
        immediate_kite_on_reached = true,
        kite_radius = 2600,
        kite_point_count = 3,
        kite_switch_ms = 2400,
        seamless_kite = true,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        generic_followup_refresh_ms = 3500,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        task_patterns = {
            "\u{53CD}\u{51FB}\u{7684}\u{9ECE}\u{660E}",
            "\u{5DE8}\u{578B}\u{8815}\u{866B}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        },
        exclude_task_detail_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}",
            "\u{89E3}\u{6551}\u{88AB}\u{56F0}\u{8005}"
        },
        revive_reentry = make_revive_reentry_config({
            key = "counterattack_dawn_worm_room_reentry_17624_18584",
            label = "\u{53CD}\u{51FB}\u{7684}\u{9ECE}\u{660E} Boss\u{91CD}\u{8FDB}\u{623F}",
            anchor = {
                x = 17624.50,
                y = 18583.58,
                z = 920.00,
                radius = 560,
                z_tolerance = 260
            },
            interact_distance = 360,
            portal_scan_distance = 900,
            retry_ms = 900,
            settle_ms = 1400,
            timeout_ms = 45000,
            post_transition_boss_engage_ms = 16000,
            fallback_interact = true
        })
    }),
    Actions.make_clear_room_point({
        key = "old_dusk_lai_an_boss_room_14861_23378",
        x = 14861.00,
        y = 23378.00,
        z = 1010.00,
        radius = 1900,
        trigger_distance = 900,
        immediate_kite_on_reached = true,
        kite_radius = 1260,
        kite_point_count = 4,
        kite_points = {
            { x = 16403.77, y = 22274.54, z = 1010.00 },
            { x = 15962.84, y = 21372.17, z = 1010.00 },
            { x = 15437.69, y = 22011.30, z = 1010.00 },
            { x = 15477.07, y = 22628.29, z = 1010.00 }
        },
        kite_switch_ms = 2200,
        seamless_kite = true,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        generic_followup_refresh_ms = 3500,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        ignore_terminal_text_change_when_objective_same = true,
        task_patterns = {
            "\u{65E7}\u{65E5}\u{7684}\u{9EC4}\u{660F}"
        },
        task_detail_patterns = {
            "\u{8FFD}\u{51FB}\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        },
        revive_reentry = make_revive_reentry_config({
            key = "old_dusk_lai_an_boss_room_reentry_14861_23378",
            label = "\u{65E7}\u{65E5}\u{7684}\u{9EC4}\u{660F} Boss\u{91CD}\u{8FDB}\u{623F}",
            anchor = {
                x = 14864.95,
                y = 23355.08,
                z = 1010.00,
                radius = 620,
                z_tolerance = 320
            },
            waypoints = {
                { x = 15214.98, y = 19127.11, z = 1010.00 },
                { x = 14445.89, y = 19340.83, z = 1010.00 },
                { x = 13901.46, y = 19691.01, z = 1010.00 },
                { x = 13595.97, y = 20203.03, z = 1010.00 },
                { x = 13451.89, y = 20632.95, z = 1010.00 },
                { x = 13380.09, y = 21374.39, z = 1010.00 },
                { x = 13463.10, y = 21894.64, z = 1010.00 },
                { x = 13720.87, y = 22486.74, z = 1010.00 },
                { x = 14001.11, y = 23053.35, z = 1010.00 },
                { x = 14401.45, y = 23445.98, z = 1010.00 },
                { x = 14864.95, y = 23355.08, z = 1010.00 }
            },
            waypoint_reach_radius = 260,
            use_global_portal = false,
            interact_distance = 360,
            call_task_before_reentry = false,
            follow_task_path_to_anchor = false,
            portal_scan_distance = 900,
            retry_ms = 900,
            settle_ms = 1400,
            timeout_ms = 90000,
            transition_success_room_radius = 2100,
            post_transition_boss_engage_ms = 18000,
            fallback_interact = true
        })
    }),
    Actions.make_objective_point({
        key = "dragonbone_griffin_boss_room",
        x = 2918.38,
        y = 4595.53,
        z = 1192.00,
        radius = 1300,
        kite_radius = 1260,
        trigger_distance = 900,
        immediate_kite_on_reached = true,
        seamless_kite = true,
        kite_switch_ms = 2200,
        kite_arrive_distance = 420,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 2500,
        generic_followup_refresh_ms = 3000,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        kite_point_count = 3,
        ignore_terminal_text_change_when_objective_same = true,
        revive_reentry = make_revive_reentry_config({
            key = "dragonbone_griffin_reentry_1891_6171_objective",
            label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}\u{72EE}\u{9E6B}Boss\u{91CD}\u{8FDB}\u{623F}",
            anchor = {
                x = 1891.00,
                y = 6171.00,
                z = 1192.00,
                radius = 560
            },
            interact_distance = 300,
            portal_scan_distance = 900,
            retry_ms = 900,
            settle_ms = 1400,
            timeout_ms = 22000,
            post_transition_boss_engage_ms = 16000,
            fallback_interact = true
        }),
        task_patterns = {
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}",
            "\u{4E3B}\u{7EBF} \u{9F99}\u{9668}\u{4E4B}\u{91CE}"
        },
        task_detail_patterns = {
            "\u{6DF1}\u{5165}\u{9F99}\u{9AA8}\u{5C71}\u{810A}\u{8179}\u{5730}",
            "\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        },
        exclude_task_detail_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    }),
    Actions.make_objective_point({
        key = "plateau_dragonbone_beast_boss_room",
        x = 9674.74,
        y = 25189.26,
        z = 5215.00,
        radius = 1200,
        kite_radius = 1260,
        kite_point_count = 3,
        trigger_distance = 900,
        immediate_kite_on_reached = true,
        seamless_kite = true,
        kite_switch_ms = 2200,
        kite_arrive_distance = 420,
        kite_move_interval_ms = 120,
        boss_clear_settle_ms = 2500,
        generic_followup_refresh_ms = 3500,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        ignore_terminal_text_change_when_objective_same = true,
        allow_when_task_unknown = true,
        revive_reentry = make_plateau_dragonbone_beast_reentry_config(),
        task_patterns = {
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}",
            "\u{4E3B}\u{7EBF} \u{9F99}\u{9668}\u{4E4B}\u{91CE}"
        },
        task_detail_patterns = {
            "\u{5BFB}\u{627E}\u{77EE}\u{4EBA}\u{56FD}\u{5EA6}\u{5165}\u{53E3}",
            "\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{5730}\u{884C}\u{5F02}\u{517D}",
            "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{5F02}\u{517D}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    }),
    Actions.make_clear_room_point({
        key = "empire_ashes_head_wolf_dodge_boss_room",
        x = 12752.00,
        y = 16096.00,
        z = 906.00,
        radius = 1400,
        trigger_distance = 900,
        kite_radius = 2600,
        kite_switch_ms = 2800,
        boss_clear_settle_ms = 3000,
        defer_followup_until_clear = true,
        ignore_terminal_text_change_when_objective_same = true,
        kite_points = {
            { x = 13536.51, y = 15323.40, z = 906.00 },
            { x = 13201.13, y = 16535.58, z = 906.00 },
            { x = 12125.71, y = 16047.29, z = 906.00 }
        },
        task_patterns = {
            "\u{5E1D}\u{56FD}\u{4F59}\u{7130}",
            "\u{5BFB}\u{627E}\u{7FA4}\u{72FC}\u{5E2E}\u{9996}\u{9886}"
        },
        task_detail_patterns = {
            "\u{6253}\u{8D25}\u{201C}\u{5934}\u{72FC}\u{201D}\u{9053}\u{5947}",
            "\u{5BFB}\u{627E}\u{7FA4}\u{72FC}\u{5E2E}\u{9996}\u{9886}"
        },
        constraint_mode = "all",
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    }),
    Actions.make_clear_room_point({
        key = "fire_treader_romel_boss_room",
        x = 3550.00,
        y = -50.00,
        z = 6.00,
        radius = 1400,
        trigger_distance = 900,
        kite_radius = 0,
        kite_switch_ms = 2400,
        boss_clear_settle_ms = 3000,
        kite_points = {
            { x = 3550.00, y = -50.00, z = 6.00 },
            { x = 3550.00, y = -50.00, z = 6.00 },
            { x = 3550.00, y = -50.00, z = 6.00 },
            { x = 3550.00, y = -50.00, z = 6.00 }
        },
        task_patterns = {
            "\u{8E48}\u{706B}\u{4E4B}\u{4EBA}",
            "\u{51FB}\u{8D25}\u{88AB}\u{9B54}\u{6CD5}\u{5B66}\u{9662}\u{6539}\u{9020}\u{7684}\u{7F57}\u{6885}\u{5C14}",
            "\u{5B9E}\u{9A8C}\u{4F53}\u{00B7}\u{7F57}\u{6885}\u{5C14}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{88AB}\u{9B54}\u{6CD5}\u{5B66}\u{9662}\u{6539}\u{9020}\u{7684}\u{7F57}\u{6885}\u{5C14}",
            "\u{5B9E}\u{9A8C}\u{4F53}\u{00B7}\u{7F57}\u{6885}\u{5C14}"
        },
        constraint_mode = "all",
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    }),
    Actions.make_clear_room_point({
        key = "tianqian_cross_wall_room_8726_13110",
        x = 8726.00,
        y = 13110.00,
        z = 86.00,
        radius = 1200,
        trigger_distance = 1100,
        immediate_kite_on_reached = true,
        kite_radius = 2200,
        kite_switch_ms = 2400,
        seamless_kite = true,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        generic_followup_refresh_ms = 3500,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        kite_points = {
            { x = 7815.17, y = 13654.82, z = 86.00 },
            { x = 7996.41, y = 12194.96, z = 86.00 },
            { x = 9564.06, y = 12196.62, z = 86.00 },
            { x = 9654.52, y = 13564.38, z = 86.00 }
        },
        task_patterns = {
            "\u{5929}\u{5811}\u{6B67}\u{8DEF}"
        },
        task_detail_patterns = {
            "\u{7EE7}\u{7EED}\u{524D}\u{8FDB}\u{FF0C}\u{7A7F}\u{8D8A}\u{5DE8}\u{5899}"
        },
        exclude_task_detail_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all"
    }),
    Actions.make_clear_room_point({
        key = "tianqian_guard_cannon_awakened_room_-2451_1966",
        x = -2451.30,
        y = 1965.61,
        z = 2461.00,
        radius = 1200,
        trigger_distance = 900,
        immediate_kite_on_reached = true,
        kite_radius = 2600,
        kite_switch_ms = 2400,
        seamless_kite = true,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        generic_followup_refresh_ms = 3500,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        kite_points = {
            { x = -1446.32, y = 3412.12, z = 2446.00 },
            { x = -894.30,  y = 4426.68, z = 2446.00 },
            { x = 59.81,    y = 3793.43, z = 2446.00 },
            { x = -813.77,  y = 2941.04, z = 2446.00 }
        },
        task_patterns = {
            "\u{5929}\u{5811}\u{6B67}\u{8DEF}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{5B88}\u{62A4}\u{5DE8}\u{70AE}\u{7684}\u{89C9}\u{9192}\u{8005}"
        },
        exclude_task_detail_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all"
    }),
    Actions.make_clear_room_point({
        key = "shadowland_mutated_liv_boss_room",
        x = 8619.03,
        y = 8740.66,
        z = 1231.00,
        radius = 1600,
        trigger_distance = 1200,
        kite_radius = 3200,
        kite_switch_ms = 2800,
        boss_clear_settle_ms = 3000,
        kite_points = {
            { x = 9705.00, y = 8757.00, z = 1231.00 },
            { x = 8222.76, y = 9810.29, z = 1231.00 },
            { x = 7929.32, y = 7654.69, z = 1231.00 }
        },
        task_patterns = {
            "\u{71C3}\u{70E7}\u{7684}\u{957F}\u{591C}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{5F02}\u{5316}\u{7684}\u{4E3D}\u{8299}"
        },
        constraint_mode = "all",
        revive_reentry = make_revive_reentry_config({
            key = "shadowland_mutated_liv_boss_room_reentry_6409_6476",
            label = "\u{9634}\u{7FF3}\u{4E4B}\u{5730} \u{42}\u{6F}\u{73}\u{73}\u{91CD}\u{8FDB}\u{623F}",
            anchor = {
                x = 6409.15,
                y = 6475.82,
                z = 1231.00,
                radius = 560
            },
            interact_distance = 280,
            retry_ms = 1200,
            settle_ms = 1400,
            timeout_ms = 20000,
            post_transition_boss_engage_ms = 16000,
            fallback_interact = true
        })
    }),
    Actions.make_objective_point({
        key = "lost_mine_depth_boss_room",
        x = 12926.60,
        y = 4337.74,
        z = -2094.00,
        radius = 1200,
        kite_radius = 3600,
        trigger_distance = 900,
        allow_nearby_text_task_change_exit = true,
        nearby_text_task_change_confirm_ms = 300,
        nearby_text_task_change_confirm_count = 1,
        nearby_text_task_change_exit_patterns = {
            "\u{7A7F}\u{8D8A}\u{5931}\u{843D}\u{77FF}\u{9053}"
        },
        task_patterns = {
            "\u{524D}\u{5F80}\u{5931}\u{843D}\u{77FF}\u{9053}\u{6DF1}\u{5904}",
            "\u{51FB}\u{8D25}\u{5730}\u{9B54}\u{9996}\u{9886}",
            "\u{4E3B}\u{7EBF} \u{65E0}\u{5149}\u{56FD}\u{5EA6}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    })
}

local function maintenance_step_has_include_pattern(step, needle)
    if type(step) ~= "table" or needle == nil then
        return false
    end
    local patterns = step.include_patterns
    if type(patterns) ~= "table" then
        return false
    end
    for _, pattern in ipairs(patterns) do
        if tostring(pattern or ""):find(tostring(needle), 1, true) ~= nil then
            return true
        end
    end
    return false
end

local function apply_talent_node_click_fallback_defaults()
    local cfg = type(M.LEVEL_UP_MAINTENANCE_CONFIG) == "table" and M.LEVEL_UP_MAINTENANCE_CONFIG or nil
    local talent_by_level = type(cfg) == "table" and cfg.talent_by_level or nil
    if type(talent_by_level) ~= "table" then
        return
    end

    for _, plan in pairs(talent_by_level) do
        local steps = type(plan) == "table" and plan.steps or nil
        if type(steps) == "table" then
            for _, step in ipairs(steps) do
                if maintenance_step_has_include_pattern(step, "TalentPointItem_C.WidgetTree.SelectBtn")
                    and step.fixed_client_click ~= true
                then
                    local max_distance = tonumber(step.hint_max_distance)
                    if max_distance == nil or max_distance > 20 then
                        step.hint_max_distance = 20
                    end
                    if step.disable_target_poll ~= true then
                        step.target_poll_count = tonumber(step.target_poll_count) or 15
                        step.target_poll_interval_ms = tonumber(step.target_poll_interval_ms) or 100
                    end

                    local hint_client_x = tonumber(step.hint_client_x)
                    local hint_client_y = tonumber(step.hint_client_y)
                    if hint_client_x ~= nil and hint_client_y ~= nil
                        and (step.fixed_fallback_client_x == nil or step.fixed_fallback_client_y == nil)
                    then
                        step.fixed_fallback_client_x = hint_client_x
                        step.fixed_fallback_client_y = hint_client_y
                    end

                    local hint_ratio_x = tonumber(step.hint_ratio_x)
                    local hint_ratio_y = tonumber(step.hint_ratio_y)
                    if hint_ratio_x ~= nil and hint_ratio_y ~= nil
                        and (step.fixed_fallback_ratio_x == nil or step.fixed_fallback_ratio_y == nil)
                    then
                        step.fixed_fallback_ratio_x = hint_ratio_x
                        step.fixed_fallback_ratio_y = hint_ratio_y
                    end

                    step.fixed_fallback_prefer_ratio = step.fixed_fallback_prefer_ratio ~= false
                    step.fixed_fallback_mouse_mode = step.fixed_fallback_mouse_mode or "api"
                    step.fixed_fallback_hover_delay_ms = tonumber(step.fixed_fallback_hover_delay_ms) or 80
                    step.fixed_fallback_click_delay_ms = tonumber(step.fixed_fallback_click_delay_ms) or 50
                end
            end
        end
    end
end

apply_talent_node_click_fallback_defaults()

function M.validate()
    return Actions.validate_leveling_config(M)
end

return M


