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

local function configured_sun_faction_choice()
    local choice = math.floor(tonumber(read_project_config_number("avepointSunFaction", 1)) or 1)
    if choice == 2 then
        return 2
    end
    return 1
end

local SUN_FACTION_CHOICE = configured_sun_faction_choice()

M.LEVEL_UP_MAINTENANCE_CONFIG = {
    enabled = true,
    execute_ui = true,
    run_current_level_plan_on_baseline = true,
    catch_up_missing_plans_on_baseline = true,
    seed_next_missing_level_when_level_text_missing = true,
    probe_ms = 1200,
    safe_no_monster_ms = 1800,
    monster_guard_distance = 1000,
    monster_hard_block_distance = 300,
    nearby_monster_soft_observe_ms = 2000,
    nearby_monster_soft_resource_drop_epsilon = 1,
    nearby_monster_hold_timeout_ms = 7000,
    nearby_monster_defer_retry_ms = 8000,
    min_hp_ratio = 0.72,
    allow_low_hp_maintenance = true,
    allow_position_available_without_main_interface = true,
    step_wait_ms = 650,
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
    talent_enabled = true,
    contract_enabled = true,
    plan_order = { "skill", "talent", "contract" },
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
                    key = "open_skill_add_panel",
                    label = "技能加点入口按钮",
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.HomePointItem.WidgetTree.AddPanelBtn"
                    },
                    hint_client_x = 1281.297729,
                    hint_client_y = 56.706509,
                    hint_ratio_x = 0.890408,
                    hint_ratio_y = 0.063007,
                    hint_max_distance = 90,
                    wait_after_ms = 900
                },
                {
                    key = "click_skill_upgrade_image",
                    label = "技能升级找图按钮",
                    missing_image_means_done = true,
                    cleanup_back_before_finish = true,
                    repeat_image_until_missing = true,
                    repeat_image_until_missing_max_count = 30,
                    repeat_image_until_missing_interval_ms = 180,
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
                    hint_client_x = 1384.784546,
                    hint_client_y = 52.000000,
                    hint_ratio_x = 0.961656,
                    hint_ratio_y = 0.057778,
                    hint_max_distance = 90,
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
                    hint_max_distance = 35,
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
                    hint_max_distance = 35,
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
                    hint_max_distance = 35,
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
                    hint_max_distance = 35,
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
                    hint_max_distance = 35,
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
                    hint_max_distance = 80,
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
        first_center_x = 958,
        first_center_y = 570,
        last_center_x = 1392,
        last_center_y = 755,
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
    level_8_talent_plan.label = "8级天赋：激活天赋节点并点基石"
    local level_8_talent_steps = type(level_8_talent_plan.steps) == "table" and level_8_talent_plan.steps or {}
    local level_8_back_step = nil
    if #level_8_talent_steps > 0 and tostring(level_8_talent_steps[#level_8_talent_steps].key or "") == "back_from_talent_detail" then
        level_8_back_step = table.remove(level_8_talent_steps, #level_8_talent_steps)
    end
    level_8_talent_steps[#level_8_talent_steps + 1] = {
        key = "select_level_8_keystone_tab",
        label = "8级天赋基石页签按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.TabTalentItem_C.WidgetTree.KeyStoneItem1.WidgetTree.TabBtn"
        },
        hint_client_x = 361.077972,
        hint_client_y = 250.016342,
        hint_ratio_x = 0.250923,
        hint_ratio_y = 0.277796,
        hint_max_distance = 80,
        wait_after_ms = 650
    }
    level_8_talent_steps[#level_8_talent_steps + 1] = {
        key = "select_level_8_keystone_option",
        label = "8级天赋基石选择按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.TabKeyStoneItem_C.WidgetTree.SelectBtn2"
        },
        hint_client_x = 647.869873,
        hint_client_y = 633.373291,
        hint_ratio_x = 0.450222,
        hint_ratio_y = 0.703748,
        hint_max_distance = 80,
        wait_after_ms = 650
    }
    if level_8_back_step ~= nil then
        level_8_talent_steps[#level_8_talent_steps + 1] = level_8_back_step
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
            step.hint_max_distance = 80
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
            step.hint_max_distance = 80
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
            step.hint_max_distance = 80
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
                step.hint_max_distance = 80
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
            step.hint_max_distance = 80
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
                step.hint_max_distance = 80
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

    local function level_20_extra_talent_click_step(key, label, client_x, client_y, ratio_x, ratio_y, wait_after_ms)
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

    local level_20_plan = make_level_18_to_20_talent_plan(20)
    local level_20_base_steps = type(level_20_plan.steps) == "table" and level_20_plan.steps or {}
    local open_menu_step = find_plan_step(level_20_base_steps, "open_fast_entrance_menu")
    local open_talent_step = find_plan_step(level_20_base_steps, "open_talent_panel")
    local check_points_step = find_plan_step(level_20_base_steps, "check_talent_points")
    local back_step = find_plan_step(level_20_base_steps, "back_from_talent_detail")
    local level_20_steps = {
        clone_step_with_key(open_menu_step, "level_20_extra_open_fast_entrance_menu", "20级额外天赋：打开菜单"),
        clone_step_with_key(open_talent_step, "level_20_extra_open_talent_panel", "20级额外天赋：打开天赋面板"),
        clone_step_with_key(check_points_step, "level_20_extra_check_talent_points", "20级额外天赋：检查天赋点"),
        level_20_extra_talent_click_step(
            "level_20_extra_talent_tab_click",
            "20级额外天赋：点击额外页签",
            492.00,
            247.00,
            0.341904,
            0.274444,
            650
        ),
        level_20_extra_talent_click_step(
            "level_20_extra_talent_node_click",
            "20级额外天赋：点击额外节点",
            721.00,
            614.00,
            0.501042,
            0.682222,
            900
        ),
        level_20_extra_talent_click_step(
            "level_20_extra_talent_confirm_click",
            "20级额外天赋：点击额外确认",
            1227.00,
            207.00,
            0.852675,
            0.230000,
            650
        ),
        clone_step_with_key(back_step, "level_20_extra_back_from_talent_panel", "20级额外天赋：返回")
    }

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
                80,
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

    level_20_plan.key = "level_20_talent_extra_then_node_activate"
    level_20_plan.label = "20级天赋：额外点击后激活天赋节点"
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
                80,
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
                    80,
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
            maintenance_locator_step(
                "select_level_41_talent_node",
                "41级天赋：选择天赋节点",
                {
                    "UIButton Transient.GameEngine.CoreGameInstance.TalentPointItem_C.WidgetTree.SelectBtn"
                },
                1070.636841,
                616.538025,
                0.744014,
                0.685042,
                80,
                1200
            ),
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
    -- Level 60 has no talent plan after this correction.
    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[4] =
        retarget_shifted_talent_plan(original_talent_by_level[3], 4, 3)
    for level = 5, 31 do
        M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[level] =
            retarget_shifted_talent_plan(original_talent_by_level[level - 1], level, level - 1)
    end

    M.LEVEL_UP_MAINTENANCE_CONFIG.talent_by_level[20] = make_manual_talent_plan(
        20,
        "20级天赋：补位激活19级节点并执行20级前两段",
        function(steps, level)
            append_shifted_plan_steps(steps, level, original_talent_by_level[19])
            append_shifted_exact_steps(steps, level, original_talent_by_level[20], {
                "level_20_extra_open_fast_entrance_menu",
                "level_20_extra_open_talent_panel",
                "level_20_extra_check_talent_points",
                "level_20_extra_talent_tab_click",
                "level_20_extra_talent_node_click",
                "level_20_extra_talent_confirm_click",
                "level_20_extra_back_from_talent_panel",
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
end

do
    local level_5_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_5_skill_plan.key = "level_5_skill_upgrade_sequence"
    level_5_skill_plan.label = "5级技能：找图升级并配置痛楚"
    level_5_skill_plan.close_with_escape = false

    local steps = type(level_5_skill_plan.steps) == "table" and level_5_skill_plan.steps or {}
    if #steps > 0 and tostring(steps[#steps].key or "") == "back_from_skill_panel" then
        table.remove(steps, #steps)
    end
    for _, step in ipairs(steps) do
        if tostring(step.key or "") == "click_skill_upgrade_image" then
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

    steps[#steps + 1] = {
        key = "open_fast_entrance_menu_after_skill_image",
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
        key = "open_skill_panel_after_skill_image",
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
    }
    steps[#steps + 1] = fixed_click_step(
        "level_5_skill_panel_click_726_304",
        "5级技能面板固定点击1",
        726.00,
        304.00,
        0.504517,
        0.337778,
        500
    )
    steps[#steps + 1] = fixed_click_step(
        "level_5_skill_search_focus",
        "5级技能搜索输入框",
        333.00,
        654.00,
        0.231411,
        0.726667,
        250
    )
    steps[#steps + 1] = {
        kind = "type_text",
        key = "level_5_skill_search_pain_text",
        label = "输入痛楚",
        text = "痛楚",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    steps[#steps + 1] = fixed_click_step(
        "level_5_skill_search_confirm",
        "5级技能搜索确认",
        593.00,
        658.00,
        0.412092,
        0.731111,
        700
    )
    steps[#steps + 1] = fixed_click_step(
        "level_5_skill_select_pain_result",
        "5级技能选择痛楚结果",
        359.00,
        338.00,
        0.249479,
        0.375556,
        700
    )
    steps[#steps + 1] = fixed_click_step(
        "level_5_skill_extra_click_824_334",
        "5级技能额外配置点击1",
        824.00,
        334.00,
        0.572620,
        0.371111,
        350
    )
    steps[#steps + 1] = fixed_click_step(
        "level_5_skill_extra_click_715_337",
        "5级技能额外配置点击2",
        715.00,
        337.00,
        0.496873,
        0.374444,
        350
    )
    steps[#steps + 1] = fixed_click_step(
        "level_5_skill_extra_click_532_335",
        "5级技能额外配置点击3",
        532.00,
        335.00,
        0.369701,
        0.372222,
        500
    )
    steps[#steps + 1] = fixed_click_step(
        "level_5_skill_confirm_pain_slot",
        "5级技能确认痛楚配置",
        1161.00,
        252.00,
        0.806810,
        0.280000,
        700
    )
    steps[#steps + 1] = {
        key = "back_from_skill_panel_after_pain_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1384.784546,
        hint_client_y = 52.000000,
        hint_ratio_x = 0.961656,
        hint_ratio_y = 0.057778,
        hint_max_distance = 90,
        wait_after_ms = 500
    }

    level_5_skill_plan.steps = steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[5] = level_5_skill_plan

    local level_6_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_6_skill_plan.key = "level_6_skill_add_revival_warcry_sequence"
    level_6_skill_plan.label = "6级技能：添加复苏战吼"
    level_6_skill_plan.close_with_escape = false

    local level_6_steps = type(level_6_skill_plan.steps) == "table" and level_6_skill_plan.steps or {}
    if #level_6_steps > 0 and tostring(level_6_steps[#level_6_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_6_steps, #level_6_steps)
    end
    for _, step in ipairs(level_6_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_6_open_skill_add_panel"
            step.label = "6级技能加点入口按钮"
            step.missing_target_means_plan_done = true
        elseif tostring(step.key or "") == "click_skill_upgrade_image" then
            step.key = "level_6_click_skill_upgrade_image"
            step.label = "6级技能升级找图按钮"
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

    level_6_steps[#level_6_steps + 1] = {
        key = "level_6_open_fast_entrance_menu_after_skill_image",
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
    level_6_steps[#level_6_steps + 1] = {
        key = "level_6_open_skill_panel_after_skill_image",
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
    }
    level_6_steps[#level_6_steps + 1] = fixed_click_step(
        "level_6_skill_fixed_click_80_412",
        "6级技能固定点击1",
        80.00,
        412.00,
        0.055594,
        0.457778,
        500
    )
    level_6_steps[#level_6_steps + 1] = fixed_click_step(
        "level_6_skill_fixed_click_725_456",
        "6级技能固定点击2",
        725.00,
        456.00,
        0.503822,
        0.506667,
        500
    )
    level_6_steps[#level_6_steps + 1] = fixed_click_step(
        "level_6_skill_search_focus",
        "6级技能搜索输入框",
        381.00,
        654.00,
        0.264767,
        0.726667,
        250
    )
    level_6_steps[#level_6_steps + 1] = {
        kind = "type_text",
        key = "level_6_skill_search_revival_warcry_text",
        label = "输入复苏战吼",
        text = "复苏战吼",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_6_steps[#level_6_steps + 1] = fixed_click_step(
        "level_6_skill_search_confirm",
        "6级技能搜索确认",
        594.00,
        657.00,
        0.412787,
        0.730000,
        700
    )
    level_6_steps[#level_6_steps + 1] = fixed_click_step(
        "level_6_skill_select_revival_warcry_result",
        "6级技能选择复苏战吼结果",
        354.00,
        330.00,
        0.246004,
        0.366667,
        700
    )
    level_6_steps[#level_6_steps + 1] = fixed_click_step(
        "level_6_skill_fixed_click_816_398",
        "6级技能固定点击3",
        816.00,
        398.00,
        0.567060,
        0.442222,
        350
    )
    level_6_steps[#level_6_steps + 1] = fixed_click_step(
        "level_6_skill_fixed_click_799_331",
        "6级技能固定点击4",
        799.00,
        331.00,
        0.555247,
        0.367778,
        350
    )
    level_6_steps[#level_6_steps + 1] = fixed_click_step(
        "level_6_skill_fixed_click_536_401",
        "6级技能固定点击5",
        536.00,
        401.00,
        0.372481,
        0.445556,
        700
    )
    level_6_steps[#level_6_steps + 1] = {
        key = "back_from_skill_panel_after_revival_warcry_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1384.784546,
        hint_client_y = 52.000000,
        hint_ratio_x = 0.961656,
        hint_ratio_y = 0.057778,
        hint_max_distance = 90,
        wait_after_ms = 500
    }

    level_6_skill_plan.steps = level_6_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[6] = level_6_skill_plan

    local level_12_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_12_skill_plan.key = "level_12_skill_add_emergency_sequence"
    level_12_skill_plan.label = "12级技能：添加应急"
    level_12_skill_plan.close_with_escape = false

    local level_12_steps = type(level_12_skill_plan.steps) == "table" and level_12_skill_plan.steps or {}
    if #level_12_steps > 0 and tostring(level_12_steps[#level_12_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_12_steps, #level_12_steps)
    end
    for _, step in ipairs(level_12_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_12_open_skill_add_panel"
            step.label = "12级技能加点入口按钮"
            step.missing_target_means_plan_done = true
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
    level_12_steps[#level_12_steps + 1] = fixed_click_step(
        "level_12_skill_fixed_click_78_307",
        "12级技能固定点击1",
        78.00,
        307.00,
        0.054204,
        0.341111,
        500
    )
    level_12_steps[#level_12_steps + 1] = fixed_click_step(
        "level_12_skill_fixed_click_722_297",
        "12级技能固定点击2",
        722.00,
        297.00,
        0.501737,
        0.330000,
        500
    )
    level_12_steps[#level_12_steps + 1] = fixed_click_step(
        "level_12_skill_search_focus",
        "12级技能搜索输入框",
        385.00,
        657.00,
        0.267547,
        0.730000,
        250
    )
    level_12_steps[#level_12_steps + 1] = {
        kind = "type_text",
        key = "level_12_skill_search_emergency_text",
        label = "输入应急",
        text = "应急",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_12_steps[#level_12_steps + 1] = fixed_click_step(
        "level_12_skill_search_confirm",
        "12级技能搜索确认",
        593.00,
        658.00,
        0.412092,
        0.731111,
        700
    )
    level_12_steps[#level_12_steps + 1] = fixed_click_step(
        "level_12_skill_select_emergency_result",
        "12级技能选择应急结果",
        351.00,
        338.00,
        0.243919,
        0.375556,
        700
    )
    level_12_steps[#level_12_steps + 1] = fixed_click_step(
        "level_12_skill_fixed_click_811_337",
        "12级技能固定点击3",
        811.00,
        337.00,
        0.563586,
        0.374444,
        350
    )
    level_12_steps[#level_12_steps + 1] = fixed_click_step(
        "level_12_skill_fixed_click_715_331",
        "12级技能固定点击4",
        715.00,
        331.00,
        0.496873,
        0.367778,
        350
    )
    level_12_steps[#level_12_steps + 1] = fixed_click_step(
        "level_12_skill_fixed_click_547_335",
        "12级技能固定点击5",
        547.00,
        335.00,
        0.380125,
        0.372222,
        700
    )
    level_12_steps[#level_12_steps + 1] = {
        key = "back_from_skill_panel_after_emergency_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1384.784546,
        hint_client_y = 52.000000,
        hint_ratio_x = 0.961656,
        hint_ratio_y = 0.057778,
        hint_max_distance = 90,
        wait_after_ms = 500
    }

    level_12_skill_plan.steps = level_12_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[12] = level_12_skill_plan

    local level_14_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_14_skill_plan.key = "level_14_skill_upgrade_sequence"
    level_14_skill_plan.label = "14级技能：找图升级并配置范围"
    level_14_skill_plan.close_with_escape = false

    local level_14_steps = type(level_14_skill_plan.steps) == "table" and level_14_skill_plan.steps or {}
    if #level_14_steps > 0 and tostring(level_14_steps[#level_14_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_14_steps, #level_14_steps)
    end
    for _, step in ipairs(level_14_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_14_open_skill_add_panel"
            step.label = "14级技能加点入口按钮"
            step.missing_target_means_plan_done = true
        elseif tostring(step.key or "") == "click_skill_upgrade_image" then
            step.key = "level_14_click_skill_upgrade_image"
            step.label = "14级技能升级找图按钮"
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

    level_14_steps[#level_14_steps + 1] = {
        key = "level_14_open_fast_entrance_menu_after_skill_image",
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
    level_14_steps[#level_14_steps + 1] = {
        key = "level_14_open_skill_panel_after_skill_image",
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
    level_14_steps[#level_14_steps + 1] = fixed_click_step(
        "level_14_skill_fixed_click_590_372_a",
        "14级技能固定点击1",
        590.00,
        372.00,
        0.410007,
        0.413333,
        350
    )
    level_14_steps[#level_14_steps + 1] = fixed_click_step(
        "level_14_skill_fixed_click_590_372_b",
        "14级技能固定点击2",
        590.00,
        372.00,
        0.410007,
        0.413333,
        350
    )
    level_14_steps[#level_14_steps + 1] = fixed_click_step(
        "level_14_skill_search_focus",
        "14级技能搜索输入框",
        343.00,
        652.00,
        0.238360,
        0.724444,
        250
    )
    level_14_steps[#level_14_steps + 1] = {
        kind = "type_text",
        key = "level_14_skill_search_range_text",
        label = "输入范围",
        text = "范围",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_14_steps[#level_14_steps + 1] = fixed_click_step(
        "level_14_skill_search_confirm",
        "14级技能搜索确认",
        593.00,
        657.00,
        0.412092,
        0.730000,
        700
    )
    level_14_steps[#level_14_steps + 1] = fixed_click_step(
        "level_14_skill_select_range_result",
        "14级技能选择范围结果",
        356.00,
        333.00,
        0.247394,
        0.370000,
        700
    )
    level_14_steps[#level_14_steps + 1] = fixed_click_step(
        "level_14_skill_fixed_click_821_365",
        "14级技能固定点击3",
        821.00,
        365.00,
        0.570535,
        0.405556,
        350
    )
    level_14_steps[#level_14_steps + 1] = fixed_click_step(
        "level_14_skill_fixed_click_710_329",
        "14级技能固定点击4",
        710.00,
        329.00,
        0.493398,
        0.365556,
        350
    )
    level_14_steps[#level_14_steps + 1] = fixed_click_step(
        "level_14_skill_fixed_click_535_364",
        "14级技能固定点击5",
        535.00,
        364.00,
        0.371786,
        0.404444,
        700
    )
    level_14_steps[#level_14_steps + 1] = {
        key = "back_from_skill_panel_after_range_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1384.784546,
        hint_client_y = 52.000000,
        hint_ratio_x = 0.961656,
        hint_ratio_y = 0.057778,
        hint_max_distance = 90,
        wait_after_ms = 500
    }

    level_14_skill_plan.steps = level_14_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[14] = level_14_skill_plan

    local level_21_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_21_skill_plan.key = "level_21_skill_upgrade_sequence"
    level_21_skill_plan.label = "21级技能：找图升级并配置钝化"
    level_21_skill_plan.close_with_escape = false

    local level_21_steps = type(level_21_skill_plan.steps) == "table" and level_21_skill_plan.steps or {}
    if #level_21_steps > 0 and tostring(level_21_steps[#level_21_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_21_steps, #level_21_steps)
    end
    for _, step in ipairs(level_21_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_21_open_skill_add_panel"
            step.label = "21级技能加点入口按钮"
            step.missing_target_means_plan_done = true
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
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_fixed_click_587_538_a",
        "21级技能固定点击1",
        587.00,
        538.00,
        0.407922,
        0.597778,
        350
    )
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_fixed_click_587_538_b",
        "21级技能固定点击2",
        587.00,
        538.00,
        0.407922,
        0.597778,
        350
    )
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_search_focus",
        "21级技能搜索输入框",
        355.00,
        651.00,
        0.246699,
        0.723333,
        250
    )
    level_21_steps[#level_21_steps + 1] = {
        kind = "type_text",
        key = "level_21_skill_search_blunt_text",
        label = "输入钝化",
        text = "钝化",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_search_confirm",
        "21级技能搜索确认",
        597.00,
        657.00,
        0.414871,
        0.730000,
        700
    )
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_select_blunt_result",
        "21级技能选择钝化结果",
        352.00,
        332.00,
        0.244614,
        0.368889,
        700
    )
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_fixed_click_812_350",
        "21级技能固定点击3",
        812.00,
        350.00,
        0.564281,
        0.388889,
        350
    )
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_fixed_click_716_324",
        "21级技能固定点击4",
        716.00,
        324.00,
        0.497568,
        0.360000,
        350
    )
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_fixed_click_536_337",
        "21级技能固定点击5",
        536.00,
        337.00,
        0.372481,
        0.374444,
        500
    )
    level_21_steps[#level_21_steps + 1] = fixed_click_step(
        "level_21_skill_confirm_blunt_slot",
        "21级技能确认钝化配置",
        1156.00,
        250.00,
        0.803336,
        0.277778,
        700
    )
    level_21_steps[#level_21_steps + 1] = {
        key = "back_from_skill_panel_after_blunt_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1384.784546,
        hint_client_y = 52.000000,
        hint_ratio_x = 0.961656,
        hint_ratio_y = 0.057778,
        hint_max_distance = 90,
        wait_after_ms = 500
    }

    level_21_skill_plan.steps = level_21_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[21] = level_21_skill_plan

    local level_29_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_29_skill_plan.key = "level_29_skill_upgrade_sequence"
    level_29_skill_plan.label = "29级技能：找图升级并配置附加腐蚀"
    level_29_skill_plan.close_with_escape = false

    local level_29_steps = type(level_29_skill_plan.steps) == "table" and level_29_skill_plan.steps or {}
    if #level_29_steps > 0 and tostring(level_29_steps[#level_29_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_29_steps, #level_29_steps)
    end
    for _, step in ipairs(level_29_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_29_open_skill_add_panel"
            step.label = "29级技能加点入口按钮"
            step.missing_target_means_plan_done = true
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
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_fixed_click_82_359",
        "29级技能固定点击1",
        82.00,
        359.00,
        0.056984,
        0.398889,
        350
    )
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_fixed_click_724_299",
        "29级技能固定点击2",
        724.00,
        299.00,
        0.503127,
        0.332222,
        350
    )
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_fixed_click_722_300",
        "29级技能固定点击3",
        722.00,
        300.00,
        0.501737,
        0.333333,
        350
    )
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_search_focus",
        "29级技能搜索输入框",
        382.00,
        654.00,
        0.265462,
        0.726667,
        250
    )
    level_29_steps[#level_29_steps + 1] = {
        kind = "type_text",
        key = "level_29_skill_search_corrosion_text",
        label = "输入附加腐蚀",
        text = "附加腐蚀",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_search_confirm",
        "29级技能搜索确认",
        594.00,
        656.00,
        0.412787,
        0.728889,
        700
    )
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_select_corrosion_result",
        "29级技能选择附加腐蚀结果",
        350.00,
        331.00,
        0.243224,
        0.367778,
        700
    )
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_fixed_click_815_351",
        "29级技能固定点击4",
        815.00,
        351.00,
        0.566366,
        0.390000,
        350
    )
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_fixed_click_714_330",
        "29级技能固定点击5",
        714.00,
        330.00,
        0.496178,
        0.366667,
        350
    )
    level_29_steps[#level_29_steps + 1] = fixed_click_step(
        "level_29_skill_fixed_click_536_350",
        "29级技能固定点击6",
        536.00,
        350.00,
        0.372481,
        0.388889,
        700
    )
    level_29_steps[#level_29_steps + 1] = {
        key = "back_from_skill_panel_after_corrosion_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1384.784546,
        hint_client_y = 52.000000,
        hint_ratio_x = 0.961656,
        hint_ratio_y = 0.057778,
        hint_max_distance = 90,
        wait_after_ms = 500
    }

    level_29_skill_plan.steps = level_29_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[29] = level_29_skill_plan

    local level_40_skill_plan = clone_plain_table(M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[3])
    level_40_skill_plan.key = "level_40_skill_upgrade_sequence"
    level_40_skill_plan.label = "40级技能：找图升级并配置侵蚀"
    level_40_skill_plan.close_with_escape = false

    local level_40_steps = type(level_40_skill_plan.steps) == "table" and level_40_skill_plan.steps or {}
    if #level_40_steps > 0 and tostring(level_40_steps[#level_40_steps].key or "") == "back_from_skill_panel" then
        table.remove(level_40_steps, #level_40_steps)
    end
    for _, step in ipairs(level_40_steps) do
        if tostring(step.key or "") == "open_skill_add_panel" then
            step.key = "level_40_open_skill_add_panel"
            step.label = "40级技能加点入口按钮"
            step.missing_target_means_plan_done = true
        elseif tostring(step.key or "") == "click_skill_upgrade_image" then
            step.key = "level_40_click_skill_upgrade_image"
            step.label = "40级技能升级找图按钮"
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

    level_40_steps[#level_40_steps + 1] = {
        key = "level_40_open_fast_entrance_menu_after_skill_image",
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
    level_40_steps[#level_40_steps + 1] = {
        key = "level_40_open_skill_panel_after_skill_image",
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
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_fixed_click_82_531_a",
        "40级技能固定点击1",
        82.00,
        531.00,
        0.056984,
        0.590000,
        350
    )
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_fixed_click_82_531_b",
        "40级技能固定点击2",
        82.00,
        531.00,
        0.056984,
        0.590000,
        350
    )
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_fixed_click_82_531_c",
        "40级技能固定点击3",
        82.00,
        531.00,
        0.056984,
        0.590000,
        350
    )
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_fixed_click_727_455",
        "40级技能固定点击4",
        727.00,
        455.00,
        0.505212,
        0.505556,
        350
    )
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_search_focus",
        "40级技能搜索输入框",
        388.00,
        653.00,
        0.269632,
        0.725556,
        250
    )
    level_40_steps[#level_40_steps + 1] = {
        kind = "type_text",
        key = "level_40_skill_search_erosion_text",
        label = "输入侵蚀",
        text = "侵蚀",
        input_method = "clipboard",
        clear_before = true,
        key_delay_ms = 30,
        wait_after_ms = 500
    }
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_search_confirm",
        "40级技能搜索确认",
        590.00,
        655.00,
        0.410007,
        0.727778,
        700
    )
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_select_erosion_result",
        "40级技能选择侵蚀结果",
        354.00,
        329.00,
        0.246004,
        0.365556,
        700
    )
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_fixed_click_812_423",
        "40级技能固定点击5",
        812.00,
        423.00,
        0.564281,
        0.470000,
        350
    )
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_fixed_click_711_325",
        "40级技能固定点击6",
        711.00,
        325.00,
        0.494093,
        0.361111,
        350
    )
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_fixed_click_535_421",
        "40级技能固定点击7",
        535.00,
        421.00,
        0.371786,
        0.467778,
        350
    )
    level_40_steps[#level_40_steps + 1] = fixed_click_step(
        "level_40_skill_fixed_click_717_845",
        "40级技能固定点击8",
        717.00,
        845.00,
        0.498263,
        0.938889,
        700
    )
    level_40_steps[#level_40_steps + 1] = {
        key = "back_from_skill_panel_after_erosion_setup",
        label = "技能返回按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Skill_C.WidgetTree.UITitleItem.WidgetTree.BtnBack"
        },
        hint_client_x = 1384.784546,
        hint_client_y = 52.000000,
        hint_ratio_x = 0.961656,
        hint_ratio_y = 0.057778,
        hint_max_distance = 90,
        wait_after_ms = 500
    }

    level_40_skill_plan.steps = level_40_steps
    M.LEVEL_UP_MAINTENANCE_CONFIG.skill_by_level[40] = level_40_skill_plan
end

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
        key = "daylight_rivalry_after_faction_join_route_11196_512",
        label = "与日争辉_加入阵营后短路线",
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
        timeout_ms = 30000,
        waypoint_reach_radius = 240,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 11195.63, y = 512.00, z = 501.00 },
            { x = 12422.91, y = 668.20, z = 501.00 }
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

local function make_daylight_rivalry_grand_arena_loop_route_action()
    return make_route_point_action({
        key = "daylight_rivalry_grand_arena_loop_route",
        label = "与日争辉_挑战大竞技场_循环跑打直到任务刷新",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "daylight_rivalry_grand_arena_loop_route",
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "挑战大竞技场"
        },
        constraint_mode = "all",
        trigger = {
            x = 43599.45,
            y = 13004.19,
            z = 406.00,
            radius = 1800,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        waypoints = {
            { x = 43599.45, y = 13004.19, z = 406.00 },
            { x = 44831.65, y = 13856.99, z = 406.00 },
            { x = 46130.02, y = 13924.12, z = 406.00 },
            { x = 46947.59, y = 13178.95, z = 406.00 },
            { x = 47324.91, y = 12071.08, z = 406.00 },
            { x = 47031.16, y = 11068.55, z = 406.00 },
            { x = 46206.35, y = 10423.61, z = 406.00 },
            { x = 45191.07, y = 10206.47, z = 406.00 },
            { x = 44297.66, y = 10413.69, z = 406.00 },
            { x = 43781.35, y = 11146.50, z = 406.00 },
            { x = 43559.68, y = 12133.80, z = 406.00 }
        }
    })
end

local function make_daylight_rivalry_sun_champion_loop_route_action()
    return make_route_point_action({
        key = "daylight_rivalry_sun_champion_loop_route",
        label = "与日争辉_击败太阳冠军杰拉尔德_循环跑打直到任务刷新",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "daylight_rivalry_sun_champion_loop_route",
        task_patterns = {
            "与日争辉"
        },
        task_detail_patterns = {
            "击败“太阳冠军”杰拉尔德"
        },
        constraint_mode = "all",
        trigger = {
            x = 43599.45,
            y = 13004.19,
            z = 406.00,
            radius = 1800,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        waypoints = {
            { x = 43599.45, y = 13004.19, z = 406.00 },
            { x = 44831.65, y = 13856.99, z = 406.00 },
            { x = 46130.02, y = 13924.12, z = 406.00 },
            { x = 46947.59, y = 13178.95, z = 406.00 },
            { x = 47324.91, y = 12071.08, z = 406.00 },
            { x = 47031.16, y = 11068.55, z = 406.00 },
            { x = 46206.35, y = 10423.61, z = 406.00 },
            { x = 45191.07, y = 10206.47, z = 406.00 },
            { x = 44297.66, y = 10413.69, z = 406.00 },
            { x = 43781.35, y = 11146.50, z = 406.00 },
            { x = 43559.68, y = 12133.80, z = 406.00 }
        }
    })
end

local function make_daylight_rivalry_audience_queen_anchor_route_action()
    return make_route_point_action({
        key = "daylight_rivalry_audience_queen_anchor_route_47491_12143",
        label = "与日争辉_觐见女王_先回锚点再主线寻路",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
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
        timeout_ms = 45000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 47491.00, y = 12143.00, z = 406.00 }
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
                hint_max_distance = 80.000,
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
            constraint_mode = "all"
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
M.TASK_PATH_WORKER_ROUTE_MODE = true
M.TASK_PATH_USE_RAW_PATH = true
M.TASK_PATH_WORKER_MAX_POINTS = 1024
M.TASK_FOLLOW_MOVE_INTERVAL_MS = 1200
M.TASK_POS_MOVE_INTERVAL_MS = 900
M.TASK_COMBAT_KITE_ASYNC_ROUTE_WORKER = true

M.TREASURE_DUNGEON_CONFIGS = {
    {
        key = "treasure_milu_creek",
        enabled = true,
        name = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{871C}\u{9732}\u{6EAA}\u{8C37}",
        route_store_key = "treasure_milu_creek",
        target_level = 25,
        task_patterns = {
            "\u{71C3}\u{70E7}\u{7684}\u{957F}\u{591C}",
            "\u{85CF}\u{5B9D}\u{5730}",
            "\u{871C}\u{9732}\u{6EAA}\u{8C37}"
        },
        task_detail_patterns = {
            "\u{8FC7}\u{6865}\u{652F}\u{63F4}\u{4E3D}\u{8299}",
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
            radius = 320,
            z_tolerance = 260
        },
        entry_steps = {
            {
                key = "treasure_milu_creek_map_trap",
                label = "313/313\u{6309}\u{94AE}",
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
        transition_timeout_ms = 15000
    },
    {
        -- Based on the working treasure_milu_creek baseline, but this dungeon
        -- still needs real restart portal / restart landing verification.
        key = "treasure_empire_ashes_wolf_ambush_entry",
        enabled = true,
        name = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{5F85}\u{786E}\u{8BA4}(\u{5E1D}\u{56FD}\u{4F59}\u{7130})",
        route_store_key = "treasure_empire_ashes_wolf_ambush_entry",
        target_level = 38,
        inside_detect_task_panel_text = false,
        startup_recovery_restart_landing = false,
        resume_route_nearby = false,
        task_patterns = {
            "\u{5E1D}\u{56FD}\u{4F59}\u{7130}"
        },
        task_detail_patterns = {
            "\u{7A81}\u{7834}\u{7FA4}\u{72FC}\u{5E2E}\u{4F0F}\u{51FB}",
            "\u{524D}\u{5F80}\u{7FA4}\u{72FC}\u{8857}\u{5DF7}"
        },
        entry_trigger = {
            x = -827.65,
            y = 9412.00,
            z = 606.00,
            radius = 320,
            z_tolerance = 260
        },
        entry_steps = {
            {
                key = "treasure_empire_ashes_wolf_ambush_map_trap",
                label = "313/313\u{6309}\u{94AE}",
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
                    x = -827.65,
                    y = 9412.00,
                    z = 606.00,
                    radius = 160,
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
        panel_query = "\u{85CF}\u{5B9D}\u{5730}",
        panel_query_fallbacks = {
            "\u{85CF}\u{5B9D}\u{5730}",
            "\u{5E1D}\u{56FD}\u{4F59}\u{7130}",
            "\u{524D}\u{5F80}\u{7FA4}\u{72FC}\u{8857}\u{5DF7}",
            "\u{7FA4}\u{72FC}\u{8857}\u{5DF7}"
        },
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
        transition_timeout_ms = 15000,
        notes = {
            "Based on the proven treasure_milu_creek baseline, but restart portal / restart_landing are not yet verified for this dungeon",
            "Known task text: 帝国余焰 -> 前往群狼街巷",
            "Keep inside_detect_task_panel_text=false; outside detail may temporarily show 前往藏宝地：曙光大道 and must not skip entry flow",
            "Known entrance button behavior: same as previous treasure entrance button",
            "Verified inside boss anchor / kite points / exit portal from latest measured run; exit trigger moved to user F6 door anchor 16509,-12043,105 and restart trigger to 17066,-12015,105, but button F8 and real restart_landing still need verification",
            "Verified exit_landing near -827,9412,606; restart_landing still needs F7 after a real 求生之欲 restart click, so startup recovery must not use restart_landing yet",
            "Confirmed target_level=38 for return-to-mainline gate",
            "Restart/exit triggers are intentionally separated now; keep 16457.17,-12098.53 as exit-only data unless F7 proves otherwise",
            "Restart/exit portal probe now prefers hint fallback when distance-anchor locator drifts",
            "Before target_level=38 the restart portal must not fallback_interact; if the 求生之欲 MapTrapBtn cannot be located, wait for better button data instead of pressing E on the exit portal",
            "Final boss loot is intentionally capped at two pickup pulses",
            "Verify boss name_patterns / panel_query and dedicated portal button locator logs on next real run"
        }
    },
    {
        -- Enabled for entry/route capture. Boss/portal/landing data is
        -- configured, but persisted route data is still empty until first run.
        key = "treasure_new_sprout_hill_entry",
        enabled = true,
        name = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{65B0}\u{7A57}\u{5C71}\u{4E18}",
        route_store_key = "treasure_new_sprout_hill_entry_v2",
        target_level = 16,
        inside_detect_task_panel_text = false,
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
        panel_query = "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{65B0}\u{7A57}\u{5C71}\u{4E18}",
        panel_query_fallbacks = {
            "\u{85CF}\u{5B9D}\u{5730}\u{FF1A}\u{65B0}\u{7A57}\u{5C71}\u{4E18}",
            "\u{65B0}\u{7A57}\u{5C71}\u{4E18}",
            "\u{85CF}\u{5B9D}\u{5730}",
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}",
            "\u{5BFB}\u{627E}\u{77EE}\u{4EBA}\u{56FD}\u{5EA6}\u{5165}\u{53E3}"
        },
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
            loot_anchor_distance = 320,
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
        exit_landing = {
            x = 13463.00,
            y = 15847.00,
            z = 5214.00,
            radius = 2200,
            z_tolerance = 900
        },
        transition_timeout_ms = 15000,
        notes = {
            "Known mainline text: 龙陨之野 -> 寻找矮人国度入口",
            "Known side task text: 藏宝地：新穗山丘 -> 通关1次藏宝地：新穗山丘",
            "Known outside entrance: 13597,15915,5214 on 龙骨平原",
            "Entry UI steps temporarily reuse the proven treasure_milu_creek/empire entrance buttons",
            "Target level is 16; return-to-mainline gate is enabled",
            "Boss loot uses faster pickup pulses plus empty-list confirmation to avoid leaving drops behind",
            "Configured boss center/kite points, restart portal, exit portal, restart landing, and exit landing from measured F6/F7 data",
            "Exit landing updated after latest F6 sample to 13463,15847,5214; wait_exit should resume mainline from this outside point",
            "Persisted route_acquired is still false until this treasure completes its first path capture",
            "Restart landing updated to -1708,746,5929; still need F7 restart EPortal data if restart door matching drifts"
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
        target_level = 46,
        inside_detect_task_panel_text = false,
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
        path_retry_count = 5,
        path_retry_interval_ms = 1200,
        min_path_points = 3,
        acquire_path_reject_first_point = {
            x = 3050.00,
            y = 7050.00,
            z = 503.00,
            radius = 900,
            z_tolerance = 360
        },
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
        target_level = 58,
        inside_detect_task_panel_text = false,
        allow_when_task_unknown = true,
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
            radius = 420,
            z_tolerance = 260,
            allow_zero = true
        },
        restart_landing = {
            x = 0.00,
            y = 0.00,
            z = 1662.00,
            radius = 420,
            z_tolerance = 260,
            allow_zero = true
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
            radius = 420,
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
            "Restart portal trigger measured at 27603,21473,1662; exit portal trigger measured at 28042,22383,1662",
            "Exit landing reuses the same first-entry landing at 0,0,1662; runtime only treats it as exit while in exit/return flow",
            "Boss room center is the acquired treasure route destination; kite points verified at 26898.02,22608.14 / 25794.32,21898.75 / 26792.32,21265.38",
            "Entry button verified by F10 GetCurrentSelected as FightInteractiveView_C.WidgetTree.MapTrapBtn near client 706.53,707.63",
            "The nearby 末日重斧 text is ground-item noise and is intentionally not used as a locator anchor",
            "Missing: live verification of boss portal enum and loot completion after this boss config"
        }
    }
}

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
                x = 10329.00,
                y = -7267.00,
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
            ignore_terminal_text_change_when_objective_same = true
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
            trigger_distance = 1700,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
            kite_radius = 1260,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 180,
            defer_followup_until_clear = true,
            boss_clear_settle_ms = 3500,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            allow_nearby_text_task_change_exit = true,
            nearby_text_task_change_confirm_ms = 1200,
            nearby_text_task_change_confirm_count = 2,
            ignore_terminal_text_change_when_objective_same = true,
            kite_points = {
                { x = 1920.00, y = -1616.00, z = 566.00 },
                { x = 30.00, y = -524.81, z = 566.00 },
                { x = 30.00, y = -2707.19, z = 566.00 }
            }
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

local function make_escape_inner_city_hass_task_config()
    return make_boss_kite_task_config(
        "escape_inner_city_hass_room_kite",
        {
            trigger_distance = 520,
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
                { x = -17411.62, y = 6564.26, z = 1004.00 },
                { x = -17556.39, y = 5225.58, z = 1004.00 },
                { x = -16261.53, y = 4982.27, z = 1004.00 },
                { x = -16227.50, y = 6226.11, z = 1004.00 }
            }
        },
        {
            task_patterns = {
                "\u{9003}\u{79BB}\u{5185}\u{57CE}\u{533A}"
            },
            task_detail_patterns = {
                "\u{51FB}\u{8D25}\u{526F}\u{5B98}\u{54C8}\u{65AF}"
            },
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

local function make_wall_of_sighs_will_wall_boss_task_config()
    return make_boss_kite_task_config(
        "wall_of_sighs_will_wall_end_kite",
        {
            trigger_distance = 600,
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
                { x = 18648.82, y = -3124.62, z = 403.00 },
                { x = 17522.53, y = -2107.33, z = 408.00 },
                { x = 17838.49, y = -1372.60, z = 404.00 },
                { x = 19137.89, y = -1257.22, z = 403.00 },
                { x = 19428.58, y = -2196.27, z = 403.00 }
            }
        },
        {
            task_patterns = {
                "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
            },
            task_detail_patterns = {
                "\u{7EE7}\u{7EED}\u{524D}\u{8FDB}\u{FF0C}\u{7A7F}\u{8D8A}\u{610F}\u{5FD7}\u{9AD8}\u{5899}"
            },
            constraint_mode = "all"
        }
    )
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
                { x = -649.51,  y = 2742.25, z = 2446.00 },
                { x = -1642.57, y = 3729.06, z = 2446.00 },
                { x = -952.80,  y = 4962.58, z = 2446.00 },
                { x = 376.44,   y = 3825.44, z = 2446.00 }
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
                "\u{62B5}\u{8FBE}\u{56FD}\u{738B}\u{752C}\u{9053}\u{6700}\u{6DF1}\u{5904}",
                "\u{56FD}\u{738B}\u{752C}\u{9053}\u{6700}\u{6DF1}\u{5904}",
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
    })
end

local function make_mountain_heart_dwarf_king_reentry_config()
    return make_revive_reentry_config({
        key = "mountain_heart_dwarf_king_endpoint_reentry_4581_13976",
        label = "群山之心_矮人王Boss重进房",
        anchor = {
            x = 4581.00,
            y = 13976.00,
            z = 67.31,
            radius = 560
        },
        interact_distance = 280,
        retry_ms = 1200,
        settle_ms = 1400,
        timeout_ms = 20000,
        post_transition_boss_engage_ms = 16000
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
    ["追击觉醒者莱安"] = {
        boss_objective_point_key = "old_dusk_lai_an_boss_room_15980_22110",
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
    ["\u{51FB}\u{8D25}\u{88AB}\u{64CD}\u{7EB5}\u{7684}\u{54E5}\u{5E03}\u{6797}"] = make_boss_kite_task_config(
        "controlled_goblin_boss",
        {
            trigger_distance = 520,
            immediate_kite_on_reached = true,
            kite_radius = 1200,
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
                { x = 12803.00, y = -7308.00, z = 608.00 },
                { x = 11910.25, y = -6507.10, z = 607.00 },
                { x = 11391.75, y = -7504.81, z = 566.00 }
            }
        },
        {
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
                label = "\u{9F99}\u{9AA8}\u{5C71}\u{810A}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "\u{9F99}\u{9AA8}\u{5C71}\u{810A}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                distance_min = 128.003496,
                distance_max = 133.003496,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 688.675232,
                hint_client_y = 427.335968,
                hint_ratio_x = 0.478247,
                hint_ratio_y = 0.474818,
                hint_max_distance = 80.000
            },
            selection_settle_ms = 700
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
            ignore_terminal_text_change_when_objective_same = true
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
            ignore_terminal_text_change_when_objective_same = true
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
            generic_followup_require_no_special = true
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
    ["\u{524D}\u{5F80}\u{5723}\u{5FB7}\u{5170}\u{9B54}\u{6CD5}\u{5B66}\u{9662}"] = make_world_map_send_task_config(
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
            center_click_ratio_x = 0.503472,
            center_click_ratio_y = 0.495556,
            center_use_human_mouse = true,
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
            task_patterns = {
                "\u{8E48}\u{706B}\u{4E4B}\u{4EBA}"
            },
            task_detail_patterns = {
                "\u{524D}\u{5F80}\u{5723}\u{5FB7}\u{5170}\u{9B54}\u{6CD5}\u{5B66}\u{9662}"
            },
            constraint_mode = "all"
        }
    ),
    ["\u{8E48}\u{706B}\u{4E4B}\u{4EBA}"] = make_fire_treader_romel_boss_task_config(),
    ["\u{51FB}\u{8D25}\u{88AB}\u{9B54}\u{6CD5}\u{5B66}\u{9662}\u{6539}\u{9020}\u{7684}\u{7F57}\u{6885}\u{5C14}"] = make_fire_treader_romel_boss_task_config(),
    ["\u{5B9E}\u{9A8C}\u{4F53}\u{00B7}\u{7F57}\u{6885}\u{5C14}"] = make_fire_treader_romel_boss_task_config(),
    ["\u{738B}\u{56FD}\u{7EC8}\u{9014} / \u{62B5}\u{8FBE}\u{56FD}\u{738B}\u{752C}\u{9053}\u{6700}\u{6DF1}\u{5904}"] = make_kingdom_end_deep_boss_task_config(),
    ["\u{4E3B}\u{7EBF} \u{738B}\u{56FD}\u{7EC8}\u{9014} / \u{62B5}\u{8FBE}\u{56FD}\u{738B}\u{752C}\u{9053}\u{6700}\u{6DF1}\u{5904}"] = make_kingdom_end_deep_boss_task_config(),
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
    ["晚星待明 / 跟随艾丝梅拉达，前往营救晚星战俘"] = make_late_star_royal_encirclement_boss_task_config({
        detail_pattern = "跟随艾丝梅拉达，前往营救晚星战俘"
    }),
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
    ["\u{5E26}\u{4E0A}\u{79D1}\u{91CC}\u{FF0C}\u{7EE7}\u{7EED}\u{8FFD}\u{8E2A}\u{83B1}\u{5B89}"] = make_ancient_battlefield_trace_ryan_task_config(),
    ["\u{7B49}\u{5F85}\u{79D1}\u{91CC}\u{5C06}\u{6728}\u{6865}\u{4FEE}\u{597D}"] = make_ancient_battlefield_trace_ryan_task_config(),
    ["\u{63A9}\u{62A4}\u{79D1}\u{91CC}\u{4FEE}\u{6865}"] = make_ancient_battlefield_trace_ryan_task_config(),
    ["\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{526F}\u{5B98}"] = make_fanmu_blocking_deputy_task_config(),
    ["\u{51FB}\u{8D25}\u{526F}\u{5B98}\u{54C8}\u{65AF}"] = make_escape_inner_city_hass_task_config(),
    ["\u{51FB}\u{8D25}\u{9A7B}\u{5B88}\u{57CE}\u{5899}\u{7684}\u{5B66}\u{57CE}\u{5B88}\u{536B}"] = make_wall_of_sighs_city_guard_task_config(),
    ["\u{7EE7}\u{7EED}\u{524D}\u{8FDB}\u{FF0C}\u{7A7F}\u{8D8A}\u{610F}\u{5FD7}\u{9AD8}\u{5899}"] = make_wall_of_sighs_will_wall_boss_task_config(),
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
                { x = 155.47, y = 2093.02, z = 1281.00 },
                { x = 1450.77, y = 3179.78, z = 1281.00 },
                { x = -452.57, y = 4007.07, z = 1281.00 },
                { x = -800.64, y = 2554.34, z = 1281.00 }
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
            center_mouse_mode = "api",
            center_hover_delay_ms = 90,
            center_click_delay_ms = 60,
            center_settle_ms = 750,
            center_retry_ms = 1400,
            transition_wait_ms = 2500,
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
            center_click_ratio_y = 0.498889,
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
                "\u{6C38}\u{591C}\u{9E23}\u{6C99}"
            },
            task_detail_patterns = {
                "\u{524D}\u{5F80}\u{6C89}\u{6CA1}\u{6C99}\u{4E18}"
            },
            constraint_mode = "all"
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
                distance_min = 61.015775,
                distance_max = 64.789946,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 619.611877,
                hint_client_y = 299.521484,
                hint_ratio_x = 0.430585,
                hint_ratio_y = 0.332802,
                hint_max_distance = 80.000,
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
                key = "mentor_gift_dragon_scale_belt_btn",
                label = "\u{9F99}\u{9CDE}\u{62A4}\u{8170}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "\u{9F99}\u{9CDE}\u{62A4}\u{8170}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 19.166834,
                distance_max = 20.352411,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 616.611877,
                hint_client_y = 270.200287,
                hint_ratio_x = 0.428500,
                hint_ratio_y = 0.300223,
                hint_max_distance = 80.000,
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
                key = "equipment_crafting_dragon_scale_belt_btn",
                label = "\u{9F99}\u{9CDE}\u{62A4}\u{8170}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "\u{9F99}\u{9CDE}\u{62A4}\u{8170}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 19.166834,
                distance_max = 20.352411,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 616.611877,
                hint_client_y = 270.200287,
                hint_ratio_x = 0.428500,
                hint_ratio_y = 0.300223,
                hint_max_distance = 80.000,
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
                key = "otherworld_exploration_dragon_scale_belt_btn",
                label = "\u{9F99}\u{9CDE}\u{62A4}\u{8170}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "\u{9F99}\u{9CDE}\u{62A4}\u{8170}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 19.166834,
                distance_max = 20.352411,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 616.611877,
                hint_client_y = 270.200287,
                hint_ratio_x = 0.428500,
                hint_ratio_y = 0.300223,
                hint_max_distance = 80.000,
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
    ["\u{5723}\u{6D01}\u{4E4B}\u{706B}"] = make_dialogue_locator_flow_task_config(
        "holy_fire_guard_dialogue_flow",
        {
            {
                key = "holy_fire_task_detail_btn",
                label = "[\u{4EFB}\u{52A1}] \u{5723}\u{6D01}\u{4E4B}\u{706B}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "[\u{4EFB}\u{52A1}] \u{5723}\u{6D01}\u{4E4B}\u{706B}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 54.671609,
                distance_max = 58.053358,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 636.256592,
                hint_client_y = 374.421478,
                hint_ratio_x = 0.441845,
                hint_ratio_y = 0.416024,
                hint_max_distance = 80.000,
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
    ["\u{957F}\u{591C}\u{7EC8}\u{5C3D} / \u{4E0E}\u{83AB}\u{7433}\u{5A1C}\u{4EA4}\u{8C08}"] = make_dialogue_locator_flow_task_config(
        "long_night_end_task_detail_before_molina_jump",
        {
            {
                key = "long_night_end_task_detail_btn",
                label = "[\u{4EFB}\u{52A1}] \u{957F}\u{591C}\u{7EC8}\u{5C3D}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "[\u{4EFB}\u{52A1}] \u{957F}\u{591C}\u{7EC8}\u{5C3D}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button",
                distance_min = 61.015769,
                distance_max = 64.789940,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 615.611877,
                hint_client_y = 268.200287,
                hint_ratio_x = 0.427805,
                hint_ratio_y = 0.298000,
                hint_max_distance = 80.000,
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
                distance_min = 61.015769,
                distance_max = 64.789940,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 615.611877,
                hint_client_y = 272.200287,
                hint_ratio_x = 0.427805,
                hint_ratio_y = 0.302445,
                hint_max_distance = 80.000,
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
            center_click_ratio_x = 0.490972,
            center_click_ratio_y = 0.381111,
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
                "\u{6C38}\u{591C}\u{4E4B}\u{98CE}"
            },
            task_detail_patterns = {
                "\u{524D}\u{5F80}\u{906E}\u{98CE}\u{58C1}\u{969C}"
            },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
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
            center_click_ratio_x = 0.502778,
            center_click_ratio_y = 0.381111,
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
            center_click_ratio_x = 0.595833,
            center_click_ratio_y = 0.497778,
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
            center_click_ratio_x = 0.724306,
            center_click_ratio_y = 0.502222,
            center_use_human_mouse = true,
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
    ["前往锈蚀深渊"] = make_world_map_send_task_config(
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
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
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
                "谒见之路"
            },
            task_detail_patterns = {
                "前往太阳王座，觐见女王"
            },
            exclude_task_detail_patterns = {
                "交谈",
                "对话"
            },
            constraint_mode = "all"
        }
    ),
    ["击败太阳女王"] = make_boss_kite_task_config(
        "eternal_rust_defeat_sun_queen_boss_kite",
        {
            trigger_distance = 900,
            immediate_kite_on_reached = true,
            allow_no_task_target_force_kite = true,
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
                { x = 14140.00, y = 820.00, z = 1215.00 },
                { x = 13125.48, y = 743.95, z = 1215.00 },
                { x = 12998.45, y = -473.70, z = 1215.00 },
                { x = 14082.77, y = -669.93, z = 1215.00 }
            }
        },
        {
            task_patterns = {
                "永恒锈蚀"
            },
            task_detail_patterns = {
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
                distance_min = 61.059764,
                distance_max = 64.836656,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.TaskButtonDetailItem_C.WidgetTree.Button"
                },
                hint_client_x = 609.029968,
                hint_client_y = 348.017120,
                hint_ratio_x = 0.422937,
                hint_ratio_y = 0.386686,
                hint_max_distance = 80.000,
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
        main_task_call = {
            allow_anchor_click_fallback = true
        },
        enable_linear_recipe = true
    }
)
M.TASK_NAME_CONFIGS["\u{4E3B}\u{7EBF} \u{5723}\u{8BEB}\u{4E4B}\u{672B}"] = M.TASK_NAME_CONFIGS["\u{5723}\u{8BEB}\u{4E4B}\u{672B}"]

M.GUIDE_SKIP_STEP = {
    label = "鍔犳枃鎸夐挳",
    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.NoviceGuideMainUI_C.WidgetTree.C_SkipButton",
    include_patterns = {
        "UIButton Transient.GameEngine.CoreGameInstance.NoviceGuideMainUI_C.WidgetTree.C_SkipButton"
    },
    hint_client_x = 748.399780,
    hint_client_y = 123.799095,
    hint_ratio_x = 0.519722,
    hint_ratio_y = 0.137555,
    hint_max_distance = 80.000
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
    make_sun_faction_choice_action(),
    make_sun_faction_after_join_route_action(),
    make_daylight_rivalry_arena_hero_route_action(),
    make_daylight_rivalry_baptism_anchor_route_action(),
    make_daylight_rivalry_grand_arena_loop_route_action(),
    make_daylight_rivalry_sun_champion_loop_route_action(),
    make_daylight_rivalry_audience_queen_anchor_route_action(),
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
        retry_ms = 3500,
        settle_ms = 1600,
        timeout_ms = 14000,
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
        timeout_ms = 140000,
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
            { x = -13712.87, y = -1542.04, z = 1235.00 }
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
            "聆听科里",
            "矮人王"
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
        label = "上古战场_和科里交谈_无坐标NPC对话",
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
            x = 12169.00,
            y = -7323.00,
            z = 568.00,
            radius = 1800,
            z_tolerance = 320
        },
        dialogue = {
            x = 12169.00,
            y = -7323.00,
            z = 568.00,
            radius = 320,
            interact_radius = 160,
            move_interval_ms = 220,
            z_tolerance = 320,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
        }
    }),
    make_route_point_action({
        key = "wall_of_sighs_aria_dialogue_route_18660_-3654",
        label = "wall_of_sighs_aria_dialogue_pre_route",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "wall_of_sighs_aria_dialogue_npc_18827_-1574",
        task_patterns = {
            "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
        },
        task_detail_patterns = {
            "\u{548C}\u{963F}\u{745E}\u{5A05}\u{4EA4}\u{8C08}"
        },
        constraint_mode = "all",
        trigger = {
            x = 18020.00,
            y = -1950.00,
            z = 403.00,
            radius = 820,
            z_tolerance = 320
        },
        retry_ms = 600000,
        timeout_ms = 45000,
        waypoint_reach_radius = 220,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 18660.00, y = -3654.00, z = 403.00 },
            { x = 19250.00, y = -2955.00, z = 403.00 },
            { x = 18827.22, y = -1573.58, z = 403.00 }
        }
    }),
    make_npc_dialogue_route_action({
        key = "wall_of_sighs_aria_dialogue_npc_18827_-1574",
        label = "wall_of_sighs_aria_dialogue_npc",
        allow_without_task_target = true,
        direct_when_task_active = true,
        task_patterns = {
            "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
        },
        task_detail_patterns = {
            "\u{548C}\u{963F}\u{745E}\u{5A05}\u{4EA4}\u{8C08}"
        },
        constraint_mode = "all",
        trigger = {
            x = 18827.22,
            y = -1573.58,
            z = 403.00,
            radius = 900,
            z_tolerance = 320
        },
        retry_ms = 6000,
        dialogue = {
            x = 18827.22,
            y = -1573.58,
            z = 403.00,
            radius = 300,
            interact_radius = 180,
            move_interval_ms = 180,
            z_tolerance = 320,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 700,
            fallback_interact = true
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
    make_npc_dialogue_route_action({
        key = "evening_star_elsmerada_dialogue_return_-2196_4643",
        label = "\u{665A}\u{661F}\u{5F85}\u{660E}_\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}_\u{7EC8}\u{70B9}\u{540E}\u{56DE}\u{62E8}",
        task_patterns = {
            "\u{665A}\u{661F}\u{5F85}\u{660E}"
        },
        task_detail_patterns = {
            "\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all",
        trigger = {
            x = -1950.00,
            y = 5230.00,
            z = 2000.00,
            radius = 320,
            z_tolerance = 220
        },
        retry_ms = 6000,
        dialogue = {
            x = -2196.00,
            y = 4643.00,
            z = 2003.00,
            radius = 260,
            interact_radius = 140,
            move_interval_ms = 180,
            z_tolerance = 220,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 560,
            fallback_interact = true
        }
    }),
    make_npc_dialogue_route_action({
        key = "late_star_talk_to_esmeralda_npc_-679_-735",
        label = "late_star_talk_to_esmeralda_npc",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_without_task_target_only = true,
        drop_active_when_task_mismatch = true,
        retry_ms = 600000,
        task_patterns = {
            "\u{665A}\u{661F}\u{5F85}\u{660E}"
        },
        task_detail_patterns = {
            "\u{4E0E}\u{827E}\u{4E1D}\u{6885}\u{62C9}\u{8FBE}\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all",
        trigger = {
            x = -679.00,
            y = -735.00,
            z = 2009.27,
            radius = 1800,
            z_tolerance = 420
        },
        dialogue = {
            x = -679.00,
            y = -735.00,
            z = 2009.27,
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
        key = "abyss_below_open_gate_gather_2496_7984",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{627E}\u{5230}\u{6253}\u{5F00}\u{5927}\u{95E8}\u{7684}\u{65B9}\u{5F0F}_\u{7CBE}\u{786E}GatherBtn_2496_7984",
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
            x = 2496.23,
            y = 7983.64,
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
            hint_client_x = 718.204834,
            hint_client_y = 741.439941,
            hint_ratio_x = 0.498753,
            hint_ratio_y = 0.823822,
            hint_max_distance = 80.000,
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
        task_detail_patterns = {
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
        timeout_ms = 45000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 420,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -398.00, y = 25165.00, z = 509.41 },
            { x = 101.82, y = 24799.29, z = 503.00 },
            { x = 1681.99, y = 25614.40, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "abyss_below_fourth_treasure_entry_5643_-530",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{7B2C}\u{56DB}\u{85CF}\u{5B9D}\u{5730}\u{5165}\u{53E3}\u{5F15}\u{5BFC}",
        mode = "recorded_route_point",
        skip_when_treasure_completed_key = "treasure_fourth_entry_5643_-530_v1",
        skip_when_player_level_at_least = 60,
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
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 16203.00, y = 18510.00, z = 108.44 }
        }
    }),
    make_route_point_action({
        key = "empire_ashes_treasure_entry_-828_9412",
        label = "\u{5E1D}\u{56FD}\u{4F59}\u{7130}_\u{85CF}\u{5B9D}\u{5730}\u{5165}\u{53E3}\u{5F15}\u{5BFC}",
        mode = "recorded_route_point",
        skip_when_treasure_completed_key = "treasure_empire_ashes_wolf_ambush_entry",
        skip_when_player_level_at_least = 38,
        task_patterns = {
            "\u{5E1D}\u{56FD}\u{4F59}\u{7130}"
        },
        task_detail_patterns = {
            "\u{6DF1}\u{5165}\u{7FA4}\u{72FC}\u{8857}\u{5DF7}",
            "\u{7A81}\u{7834}\u{7FA4}\u{72FC}\u{5E2E}\u{4F0F}\u{51FB}"
        },
        constraint_mode = "all",
        trigger = {
            x = -827.65,
            y = 9412.00,
            z = 606.00,
            radius = 12000,
            z_tolerance = 1200
        },
        retry_ms = 8000,
        timeout_ms = 45000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -827.65, y = 9412.00, z = 606.00 }
        }
    }),
    make_route_point_action({
        key = "dragonfall_new_sprout_hill_treasure_entry_13597_15915",
        label = "\u{9F99}\u{9668}\u{4E4B}\u{91CE}_\u{65B0}\u{7A57}\u{5C71}\u{4E18}\u{85CF}\u{5B9D}\u{5730}\u{5165}\u{53E3}\u{5F15}\u{5BFC}",
        mode = "recorded_route_point",
        skip_when_treasure_completed_key = "treasure_new_sprout_hill_entry_v2",
        skip_when_player_level_at_least = 16,
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
        settle_ms = 2200,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
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
        settle_ms = 2200,
        timeout_ms = 18000,
        force_task_call_after_transition = true,
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
        key = "lionheart_aria_press_a_then_dialogue_9054_-2058",
        label = "狮心_与阿瑞娅对话_先按A五次",
        mode = "objective_button_flow_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "与阿瑞娅对话"
        },
        constraint_mode = "all",
        trigger = {
            x = 9054.00,
            y = -2058.00,
            z = 1805.00,
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
        task_patterns = {
            "狮心"
        },
        task_detail_patterns = {
            "与阿瑞娅对话"
        },
        constraint_mode = "all",
        trigger = {
            x = 9054.00,
            y = -2058.00,
            z = 1805.00,
            radius = 1900,
            z_tolerance = 520
        },
        retry_ms = 6000,
        dialogue = {
            x = 9054.00,
            y = -2058.00,
            z = 1805.00,
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
        key = "loser_badge_deep_star_road_detour_-5496_2403",
        label = "败者之证_深入繁星之路_补充录制路线",
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
            x = -5496.00,
            y = 2403.00,
            z = 502.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -6585.85, y = 2895.76, z = 502.00 },
            { x = -7416.95, y = 3247.24, z = 502.00 },
            { x = -8115.27, y = 3105.36, z = 502.00 },
            { x = -8614.34, y = 2610.57, z = 502.00 },
            { x = -8755.82, y = 1996.60, z = 502.00 },
            { x = -8453.01, y = 1430.29, z = 502.00 },
            { x = -7944.23, y = 1215.65, z = 502.00 },
            { x = -7330.97, y = 1362.10, z = 502.00 },
            { x = -6895.48, y = 1755.38, z = 502.00 },
            { x = -6671.89, y = 2231.68, z = 502.00 },
            { x = -6689.82, y = 2759.27, z = 502.00 },
            { x = -7086.81, y = 3098.98, z = 502.00 },
            { x = -7584.95, y = 3236.54, z = 502.00 },
            { x = -8122.72, y = 3090.52, z = 502.00 },
            { x = -8501.85, y = 2748.63, z = 502.00 },
            { x = -8681.52, y = 2266.19, z = 502.00 },
            { x = -8597.50, y = 1743.70, z = 502.00 },
            { x = -8282.00, y = 1438.78, z = 502.00 },
            { x = -7750.57, y = 1347.07, z = 502.00 },
            { x = -7324.80, y = 1591.97, z = 502.00 },
            { x = -7050.29, y = 2010.43, z = 502.00 },
            { x = -6839.81, y = 2457.36, z = 502.00 },
            { x = -7081.51, y = 2819.95, z = 502.00 },
            { x = -7520.78, y = 2910.79, z = 502.00 },
            { x = -7921.75, y = 2725.93, z = 502.00 },
            { x = -8105.21, y = 2334.20, z = 502.00 },
            { x = -7951.94, y = 1934.84, z = 502.00 },
            { x = -7700.69, y = 2136.07, z = 502.00 },
            { x = -7631.88, y = 2631.48, z = 502.00 },
            { x = -7978.46, y = 2700.89, z = 502.00 },
            { x = -8086.57, y = 2298.28, z = 502.00 }
        }
    }),
    make_route_point_action({
        key = "loser_badge_star_road_detour_-17521_11360",
        label = "败者之证_繁星之路_北侧补充录制路线",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        task_patterns = {
            "败者之证",
            "败者无名"
        },
        task_detail_patterns = {
            "深入繁星之路",
            "挑战繁星之路的英灵",
            "击败“不洁之星·杰拉尔德”，获得败者之证"
        },
        constraint_mode = "all",
        trigger = {
            x = -17521.00,
            y = 11360.00,
            z = 502.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -17521.00, y = 11360.00, z = 502.00 },
            { x = -17725.88, y = 12865.19, z = 502.00 },
            { x = -18409.62, y = 13345.23, z = 502.00 },
            { x = -18228.74, y = 13886.18, z = 502.00 },
            { x = -17786.90, y = 14316.75, z = 502.00 },
            { x = -17299.39, y = 14820.43, z = 502.00 },
            { x = -16803.92, y = 14864.67, z = 502.00 },
            { x = -16498.39, y = 14444.06, z = 502.00 },
            { x = -16568.89, y = 13874.52, z = 502.00 },
            { x = -16864.98, y = 13424.39, z = 502.00 },
            { x = -17267.78, y = 13007.97, z = 502.00 },
            { x = -17785.78, y = 12797.56, z = 502.00 },
            { x = -18286.64, y = 12882.84, z = 502.00 },
            { x = -18626.11, y = 13281.17, z = 502.00 },
            { x = -18165.09, y = 13642.54, z = 502.00 },
            { x = -17684.65, y = 13778.55, z = 502.00 },
            { x = -17248.62, y = 13492.26, z = 502.00 },
            { x = -17144.90, y = 13013.07, z = 502.00 },
            { x = -17513.30, y = 12750.79, z = 502.00 },
            { x = -17875.44, y = 13104.98, z = 502.00 },
            { x = -17731.94, y = 13462.63, z = 502.00 },
            { x = -17329.50, y = 13618.94, z = 502.00 },
            { x = -16879.86, y = 13614.19, z = 502.00 },
            { x = -16368.59, y = 13607.83, z = 502.00 },
            { x = -15772.62, y = 13556.71, z = 502.00 }
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
            { x = 1073.09, y = 11720.13, z = 502.00 },
            { x = 1695.34, y = 11222.76, z = 502.00 },
            { x = 2233.44, y = 11057.38, z = 502.00 },
            { x = 2771.20, y = 11187.65, z = 502.00 },
            { x = 3215.79, y = 11558.87, z = 502.00 },
            { x = 3406.88, y = 11991.53, z = 502.00 },
            { x = 3093.62, y = 12289.00, z = 502.00 },
            { x = 2811.87, y = 12639.18, z = 502.00 },
            { x = 2409.58, y = 12715.59, z = 502.00 },
            { x = 2059.59, y = 12499.85, z = 502.00 },
            { x = 1689.80, y = 12193.26, z = 502.00 },
            { x = 1346.95, y = 11913.86, z = 502.00 },
            { x = 1247.79, y = 11615.37, z = 502.00 },
            { x = 1508.88, y = 11347.49, z = 502.00 },
            { x = 1792.98, y = 11078.42, z = 502.00 },
            { x = 2148.44, y = 10933.25, z = 502.00 },
            { x = 2509.40, y = 10935.79, z = 502.00 },
            { x = 2815.45, y = 11170.36, z = 502.00 },
            { x = 3048.70, y = 11448.62, z = 502.00 },
            { x = 3273.50, y = 11736.86, z = 502.00 },
            { x = 3411.57, y = 12013.36, z = 502.00 },
            { x = 3210.79, y = 12277.68, z = 502.00 },
            { x = 2938.72, y = 12523.46, z = 502.00 },
            { x = 2592.91, y = 12698.77, z = 502.00 },
            { x = 2182.90, y = 12645.01, z = 502.00 },
            { x = 1816.45, y = 12500.61, z = 502.00 },
            { x = 1523.16, y = 12245.64, z = 502.00 },
            { x = 1339.50, y = 11932.06, z = 502.00 },
            { x = 1307.01, y = 11601.38, z = 502.00 },
            { x = 1520.37, y = 11251.86, z = 502.00 },
            { x = 1819.67, y = 11051.13, z = 502.00 },
            { x = 2172.57, y = 10966.81, z = 502.00 },
            { x = 2556.57, y = 11040.92, z = 502.00 },
            { x = 2927.73, y = 11246.59, z = 502.00 },
            { x = 3139.23, y = 11508.17, z = 502.00 },
            { x = 3377.50, y = 11817.07, z = 502.00 },
            { x = 3396.61, y = 12152.31, z = 502.00 },
            { x = 3132.86, y = 12395.91, z = 502.00 },
            { x = 2786.22, y = 12575.96, z = 502.00 },
            { x = 2412.95, y = 12683.03, z = 502.00 },
            { x = 2085.06, y = 12632.46, z = 502.00 },
            { x = 1772.11, y = 12408.75, z = 502.00 },
            { x = 1570.54, y = 12041.34, z = 502.00 },
            { x = 1460.87, y = 11698.54, z = 502.00 },
            { x = 1526.49, y = 11420.80, z = 502.00 },
            { x = 1876.08, y = 11176.03, z = 502.00 },
            { x = 2266.04, y = 11123.88, z = 502.00 },
            { x = 2628.03, y = 11148.83, z = 502.00 },
            { x = 2967.53, y = 11336.12, z = 502.00 },
            { x = 3162.04, y = 11626.32, z = 502.00 },
            { x = 3234.77, y = 11894.38, z = 502.00 },
            { x = 3126.99, y = 12268.98, z = 502.00 },
            { x = 2708.87, y = 12676.96, z = 502.00 }
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
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 2424.89, y = 19065.02, z = 502.00 },
            { x = 2258.59, y = 19890.58, z = 502.00 },
            { x = 1677.48, y = 20490.68, z = 502.00 },
            { x = 1181.39, y = 21341.69, z = 502.00 },
            { x = 1484.28, y = 21992.68, z = 502.00 },
            { x = 2018.83, y = 22181.48, z = 502.00 },
            { x = 2832.33, y = 22274.91, z = 502.00 },
            { x = 3392.93, y = 21813.79, z = 502.00 },
            { x = 3615.55, y = 21108.59, z = 502.00 },
            { x = 3415.86, y = 20532.76, z = 502.00 },
            { x = 2719.77, y = 20209.76, z = 502.00 },
            { x = 2065.84, y = 20560.05, z = 502.00 },
            { x = 2377.33, y = 21144.92, z = 502.00 },
            { x = 2566.02, y = 21676.38, z = 502.00 },
            { x = 2579.09, y = 22000.57, z = 502.00 },
            { x = 2051.16, y = 22291.46, z = 502.00 },
            { x = 1434.81, y = 22045.01, z = 502.00 },
            { x = 1159.10, y = 21390.59, z = 502.00 },
            { x = 1309.55, y = 20922.50, z = 502.00 },
            { x = 1955.69, y = 20580.59, z = 502.00 },
            { x = 2420.89, y = 20903.24, z = 502.00 },
            { x = 2385.36, y = 21675.72, z = 502.00 },
            { x = 2522.62, y = 22580.44, z = 502.00 }
        }
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
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 2349.00, y = 28144.00, z = 502.00 },
            { x = 2307.84, y = 29729.40, z = 502.00 },
            { x = 1544.31, y = 30468.86, z = 502.00 },
            { x = 1959.72, y = 31320.48, z = 502.00 },
            { x = 2606.91, y = 31083.42, z = 502.00 },
            { x = 3402.38, y = 30803.68, z = 502.00 },
            { x = 3387.09, y = 30278.14, z = 502.00 },
            { x = 2881.43, y = 29983.68, z = 502.00 },
            { x = 2346.12, y = 29805.07, z = 502.00 },
            { x = 1858.27, y = 30085.87, z = 502.00 },
            { x = 1367.38, y = 30448.44, z = 502.00 },
            { x = 1376.05, y = 30989.77, z = 502.00 },
            { x = 1805.27, y = 31482.72, z = 502.00 },
            { x = 2322.85, y = 31646.68, z = 502.00 },
            { x = 2826.89, y = 31321.95, z = 502.00 },
            { x = 3318.71, y = 30903.83, z = 502.00 },
            { x = 3468.65, y = 30518.65, z = 502.00 },
            { x = 3151.69, y = 30239.60, z = 502.00 },
            { x = 2653.31, y = 29979.12, z = 502.00 },
            { x = 2377.47, y = 29609.56, z = 502.00 },
            { x = 2326.67, y = 29157.20, z = 502.00 },
            { x = 2104.05, y = 29296.04, z = 502.00 },
            { x = 1934.22, y = 29731.56, z = 502.00 },
            { x = 1803.41, y = 30158.28, z = 502.00 },
            { x = 1892.74, y = 30701.78, z = 502.00 },
            { x = 2210.08, y = 31073.93, z = 502.00 },
            { x = 2596.44, y = 31123.25, z = 502.00 },
            { x = 3013.97, y = 31047.95, z = 502.00 },
            { x = 3442.41, y = 30885.58, z = 502.00 },
            { x = 3830.85, y = 30695.69, z = 502.00 }
        }
    }),
    make_route_point_action({
        key = "loser_badge_star_road_loop_11071_30674",
        label = "败者之证_繁星之路_终点循环路线直到任务刷新",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "loser_badge_star_road_loop_11071_30674",
        task_patterns = {
            "败者之证"
        },
        task_detail_patterns = {
            "深入繁星之路",
            "挑战繁星之路的英灵"
        },
        constraint_mode = "all",
        trigger = {
            x = 11071.00,
            y = 30674.00,
            z = 502.00,
            radius = 900,
            z_tolerance = 260
        },
        retry_ms = 600000,
        timeout_ms = 180000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 260,
        move_interval_ms = 220,
        waypoints = {
            { x = 11071.00, y = 30674.00, z = 502.00 },
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
        key = "loser_nameless_gerald_route_loop",
        label = "败者无名_杰拉尔德_循环跑打直到任务刷新",
        mode = "recorded_route_point",
        allow_without_task_target = true,
        allow_wait_task_path_recover = true,
        direct_when_task_active = true,
        complete_without_task_reacquire = true,
        followup_route_action_key = "loser_nameless_gerald_route_loop",
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
        key = "first_light_moon_road_trial_route_1050_20103",
        label = "初升之辉_皓月之路英杰_补充录制路线",
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
            x = 1050.00,
            y = 20103.00,
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
            { x = 1488.67, y = 20767.44, z = 503.00 },
            { x = 2065.48, y = 21478.79, z = 503.00 },
            { x = 2238.15, y = 22012.90, z = 503.00 },
            { x = 1895.22, y = 22474.83, z = 503.00 },
            { x = 1421.91, y = 22700.00, z = 503.00 },
            { x = 931.42, y = 22736.67, z = 503.00 },
            { x = 476.85, y = 22540.21, z = 503.00 },
            { x = 164.10, y = 22201.99, z = 503.00 },
            { x = -170.15, y = 21816.03, z = 503.00 }
        }
    }),
    make_route_point_action({
        key = "first_light_moon_road_trial_route_-8314_21824",
        label = "初升之辉_皓月之路英杰_西侧补充录制路线",
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
            x = -8314.08,
            y = 21823.55,
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
            { x = -9070.92, y = 21919.29, z = 503.00 },
            { x = -9766.75, y = 22305.42, z = 503.00 },
            { x = -10620.44, y = 22665.62, z = 503.00 },
            { x = -11420.47, y = 22254.06, z = 503.00 },
            { x = -11846.03, y = 21363.67, z = 503.00 },
            { x = -11053.43, y = 20917.03, z = 503.00 },
            { x = -10464.00, y = 20756.00, z = 503.00 }
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
        timeout_ms = 150000,
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
        key = "lionheart_power_facility_1_route_3731_-1539",
        label = "狮心_关闭供电设施_第一设施录制路线",
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
            x = 3730.95,
            y = -1539.37,
            z = 2154.22,
            radius = 2800,
            z_tolerance = 900
        },
        retry_ms = 600000,
        timeout_ms = 90000,
        waypoint_reach_radius = 260,
        waypoint_z_tolerance = 900,
        move_interval_ms = 220,
        complete_without_task_reacquire = true,
        waypoints = {
            { x = 3730.95, y = -1539.37, z = 2154.22 },
            { x = 4514.11, y = -1969.57, z = 2124.71 },
            { x = 4646.10, y = -3913.12, z = 2101.00 },
            { x = 5816.38, y = -5549.06, z = 2100.00 },
            { x = 7303.81, y = -6336.47, z = 2100.00 },
            { x = 9112.52, y = -6591.60, z = 2140.36 },
            { x = 10558.43, y = -6488.54, z = 2664.50 },
            { x = 12985.86, y = -6069.11, z = 2719.64 }
        }
    }),
    make_route_point_action({
        key = "lionheart_power_facility_1_gather_12986_-6069",
        label = "狮心_关闭供电设施_第一设施MapTrapBtn",
        mode = "objective_button_flow_point",
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
            x = 12985.86,
            y = -6069.11,
            z = 2719.64,
            radius = 760,
            z_tolerance = 520
        },
        interact_radius = 180,
        probe_retry_ms = 700,
        retry_ms = 600000,
        settle_ms = 2400,
        timeout_ms = 5200,
        timeout_followup_route_action_key = "lionheart_power_facility_2_route_13106_-5386",
        force_task_call_after_transition = false,
        step = {
            key = "lionheart_power_facility_1_map_trap_btn",
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
    make_route_point_action({
        key = "sand_sea_find_iji_fire_seed_route_10975_8556",
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
            x = 10975.00,
            y = 8556.00,
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
            { x = 11120.11, y = 8748.14, z = 16.00 },
            { x = 11711.10, y = 9045.97, z = 16.00 },
            { x = 12228.95, y = 9155.10, z = 16.00 },
            { x = 12734.58, y = 9082.60, z = 16.00 },
            { x = 13204.50, y = 8841.82, z = 16.00 },
            { x = 13657.60, y = 8517.60, z = 16.00 },
            { x = 13942.08, y = 8112.83, z = 16.00 },
            { x = 14115.23, y = 7613.25, z = 16.00 },
            { x = 14162.61, y = 7030.51, z = 16.00 },
            { x = 13999.12, y = 6517.34, z = 16.00 },
            { x = 13662.51, y = 6059.90, z = 16.00 },
            { x = 13274.36, y = 5698.42, z = 16.00 },
            { x = 12720.35, y = 5449.76, z = 16.00 },
            { x = 12211.47, y = 5392.41, z = 16.00 },
            { x = 11711.42, y = 5568.60, z = 16.00 },
            { x = 11237.40, y = 5895.88, z = 16.00 },
            { x = 10950.88, y = 6307.60, z = 16.00 },
            { x = 10766.11, y = 6799.72, z = 16.00 },
            { x = 10686.58, y = 7352.39, z = 16.00 },
            { x = 10737.20, y = 7877.82, z = 16.00 },
            { x = 10970.83, y = 8352.15, z = 16.00 },
            { x = 11355.75, y = 8790.32, z = 16.00 },
            { x = 11828.04, y = 9025.86, z = 16.00 },
            { x = 12308.97, y = 9161.57, z = 16.00 },
            { x = 12840.07, y = 9136.53, z = 16.00 },
            { x = 12420.82, y = 9185.46, z = 16.00 },
            { x = 11888.22, y = 9041.95, z = 16.00 },
            { x = 11377.16, y = 8763.87, z = 16.00 },
            { x = 10998.00, y = 8390.92, z = 16.00 },
            { x = 10695.82, y = 7956.34, z = 16.00 },
            { x = 10550.73, y = 7435.02, z = 16.00 },
            { x = 10614.72, y = 6942.19, z = 16.00 },
            { x = 10879.39, y = 6428.89, z = 16.00 },
            { x = 11193.28, y = 6147.65, z = 16.00 },
            { x = 11635.35, y = 5853.67, z = 16.00 },
            { x = 12129.34, y = 5632.81, z = 16.00 },
            { x = 12648.53, y = 5549.57, z = 16.00 },
            { x = 13137.08, y = 5645.47, z = 16.00 },
            { x = 13596.85, y = 5933.98, z = 16.00 },
            { x = 13988.32, y = 6247.21, z = 16.00 },
            { x = 14314.91, y = 6695.95, z = 16.00 },
            { x = 14473.11, y = 7224.48, z = 16.00 },
            { x = 14424.56, y = 7697.47, z = 16.00 },
            { x = 14243.06, y = 8166.76, z = 16.00 },
            { x = 13954.11, y = 8573.64, z = 16.00 },
            { x = 13527.40, y = 8886.95, z = 16.00 },
            { x = 12978.39, y = 9033.92, z = 16.00 },
            { x = 12447.24, y = 9075.82, z = 16.00 },
            { x = 11968.45, y = 8999.07, z = 16.00 },
            { x = 11470.53, y = 8813.10, z = 16.00 },
            { x = 11042.63, y = 8557.15, z = 16.00 },
            { x = 10679.69, y = 8211.42, z = 16.00 },
            { x = 10390.41, y = 7768.57, z = 16.00 },
            { x = 10549.25, y = 7337.20, z = 16.00 },
            { x = 10847.97, y = 6971.20, z = 16.00 }
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
            "\u{7EE7}\u{7EED}\u{524D}\u{8FDB}\u{FF0C}\u{7A7F}\u{8D8A}\u{610F}\u{5FD7}\u{9AD8}\u{5899}"
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
            { x = 19717.21, y = 20873.74, z = 920.00 },
            { x = 19097.61, y = 19766.45, z = 920.00 },
            { x = 18291.47, y = 20520.47, z = 920.00 }
        },
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
            key = "counterattack_dawn_worm_room_reentry_17580_18579",
            label = "\u{53CD}\u{51FB}\u{7684}\u{9ECE}\u{660E} Boss\u{91CD}\u{8FDB}\u{623F}",
            anchor = {
                x = 17580.35,
                y = 18579.16,
                z = 920.00,
                radius = 560,
                z_tolerance = 260
            },
            interact_distance = 360,
            call_task_before_reentry = true,
            follow_task_path_to_anchor = true,
            portal_scan_distance = 900,
            retry_ms = 900,
            settle_ms = 1400,
            timeout_ms = 20000,
            post_transition_boss_engage_ms = 16000,
            fallback_interact = true
        })
    }),
    Actions.make_clear_room_point({
        key = "old_dusk_lai_an_boss_room_15980_22110",
        x = 15980.00,
        y = 22110.00,
        z = 1010.00,
        radius = 1600,
        trigger_distance = 520,
        immediate_kite_on_reached = true,
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
            key = "old_dusk_lai_an_boss_room_reentry_14877_23327",
            label = "\u{65E7}\u{65E5}\u{7684}\u{9EC4}\u{660F} Boss\u{91CD}\u{8FDB}\u{623F}",
            anchor = {
                x = 14877.00,
                y = 23327.00,
                z = 1010.00,
                radius = 620
            },
            interact_distance = 280,
            handoff_distance = 980,
            retry_ms = 1200,
            settle_ms = 1400,
            timeout_ms = 22000,
            post_transition_boss_engage_ms = 16000,
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
        key = "wall_of_sighs_final_mechanism_guard_loop",
        x = 17832.21,
        y = -2009.37,
        z = 405.00,
        radius = 1900,
        trigger_distance = 900,
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
        allow_nearby_text_task_change_exit = true,
        nearby_text_task_change_confirm_ms = 1200,
        nearby_text_task_change_confirm_count = 2,
        kite_points = {
            { x = 17820.00, y = -1551.00, z = 405.00 },
            { x = 18070.56, y = -1139.69, z = 403.00 },
            { x = 18406.62, y = -1067.46, z = 403.00 },
            { x = 18965.71, y = -1216.23, z = 403.00 },
            { x = 19252.46, y = -1612.01, z = 403.00 },
            { x = 19311.52, y = -2050.16, z = 403.00 },
            { x = 19186.79, y = -2472.19, z = 403.00 },
            { x = 18871.83, y = -2808.62, z = 403.00 },
            { x = 18126.66, y = -2686.86, z = 403.00 },
            { x = 17643.00, y = -1858.00, z = 407.00 }
        },
        task_patterns = {
            "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
        },
        task_detail_patterns = {
            "\u{6D88}\u{706D}\u{5B88}\u{536B}\u{FF0C}\u{5173}\u{95ED}\u{6700}\u{7EC8}\u{673A}\u{5173}"
        },
        exclude_task_detail_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all"
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
        key = "tianqian_guard_cannon_awakened_room_433_3940",
        x = 433.00,
        y = 3940.00,
        z = 2446.00,
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
            { x = -649.51,  y = 2742.25, z = 2446.00 },
            { x = -1642.57, y = 3729.06, z = 2446.00 },
            { x = -952.80,  y = 4962.58, z = 2446.00 },
            { x = 376.44,   y = 3825.44, z = 2446.00 }
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

function M.validate()
    return Actions.validate_leveling_config(M)
end

return M


