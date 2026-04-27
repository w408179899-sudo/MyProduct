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
        loot_stuck_max_attempts = 5,
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
            radius = 2400,
            z_tolerance = 600
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
            "Verified exit_landing near -827,9412,606; restart_landing still needs F7 after a real 求生之欲 restart click",
            "Confirmed target_level=38 for return-to-mainline gate",
            "Restart/exit triggers are intentionally separated now; keep 16457.17,-12098.53 as exit-only data unless F7 proves otherwise",
            "Restart/exit portal probe now prefers hint fallback when distance-anchor locator drifts",
            "Before target_level=38 the restart portal must not fallback_interact; if the 求生之欲 MapTrapBtn cannot be located, wait for better button data instead of pressing E on the exit portal",
            "Raised loot_stuck_max_attempts for this treasure to avoid skipping boss drops after only two pulses",
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
                label = "141/141\u{6309}\u{94AE}",
                distance_anchor_exact_text = "141/141",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn",
                distance_min = 241.887232,
                distance_max = 246.887232,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.MapTrapBtn"
                },
                hint_client_x = 928.204834,
                hint_client_y = 766.439941,
                hint_ratio_x = 0.644587,
                hint_ratio_y = 0.851600,
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
            x = 13597.00,
            y = 15915.00,
            z = 5214.00,
            radius = 2600,
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
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.DialogueTalk_C.WidgetTree.JumpBtn",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.DialogueTalk_C.WidgetTree.JumpBtn"
        },
        hint_client_x = 1325.158447,
        hint_client_y = 66.883194,
        hint_ratio_x = 0.920249,
        hint_ratio_y = 0.074315,
        hint_max_distance = 80.000,
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
            kite_radius = 1800,
            kite_switch_ms = 2400,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            kite_points = {
                { x = 17723.00, y = -6545.00, z = 606.00 },
                { x = 16728.18, y = -6623.85, z = 606.00 },
                { x = 17723.00, y = -6545.00, z = 606.00 },
                { x = 16728.18, y = -6623.85, z = 606.00 }
            }
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
        key = "another_magic_academy_forbidden_guard_boss_room_reentry_18306_8595",
        label = "\u{53E6}\u{4E00}\u{4E2A}\u{9B54}\u{6CD5}\u{5B66}\u{9662} Boss\u{91CD}\u{8FDB}\u{623F}",
        anchor = {
            x = 18306.24,
            y = 8595.45,
            z = 307.57,
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
            kite_radius = 1800,
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
                { x = 19680.23, y = 8403.38, z = 607.00 },
                { x = 20886.94, y = 7818.64, z = 605.00 },
                { x = 20750.76, y = 9283.95, z = 605.00 }
            },
            revive_reentry = make_another_magic_academy_forbidden_guard_revive_reentry_config()
        },
        {
            task_patterns = {
                "\u{53E6}\u{4E00}\u{4E2A}\u{9B54}\u{6CD5}\u{5B66}\u{9662}",
                "\u{51FB}\u{8D25}\u{7981}\u{533A}\u{5B88}\u{536B}",
                "\u{5B88}\u{536B}\u{519B}\u{9886}\u{8896}\u{00B7}\u{963F}\u{5C14}\u{514B}\u{65AF}"
            },
            task_detail_patterns = {
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
            kite_radius = 0,
            kite_switch_ms = 2400,
            boss_clear_settle_ms = 3000,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            kite_points = {
                { x = 3550.00, y = -50.00, z = 6.00 },
                { x = 3550.00, y = -50.00, z = 6.00 },
                { x = 3550.00, y = -50.00, z = 6.00 },
                { x = 3550.00, y = -50.00, z = 6.00 }
            }
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

local function make_overcast_city_guard_fire_seed_device_task_config()
    return make_boss_kite_task_config(
        "overcast_city_guard_fire_seed_device_kite",
        {
            trigger_distance = 180,
            immediate_kite_on_reached = true,
            kite_radius = 1800,
            kite_switch_ms = 2400,
            seamless_kite = true,
            kite_arrive_distance = 520,
            kite_move_interval_ms = 180,
            boss_clear_settle_ms = 3500,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            kite_points = {
                { x = 5036.32, y = -177.00,  z = 3091.39 },
                { x = 4473.61, y = -1856.99, z = 3091.00 },
                { x = 5036.32, y = -177.00,  z = 3091.39 },
                { x = 4473.61, y = -1856.99, z = 3091.00 }
            }
        },
        {
            task_patterns = {
                "\u{9634}\u{4E91}\u{538B}\u{57CE}"
            },
            task_detail_patterns = {
                "\u{5B88}\u{62A4}\u{706B}\u{79CD}\u{88C5}\u{7F6E}",
                "\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}",
                "\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}"
            },
            constraint_mode = "all"
        }
    )
end

local function make_journey_begin_awakened_leader_task_config()
    return make_boss_kite_task_config(
        "journey_begin_awakened_leader_kite",
        {
            trigger_distance = 180,
            immediate_kite_on_reached = true,
            kite_radius = 1800,
            kite_switch_ms = 1200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 180,
            boss_clear_settle_ms = 3500,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            kite_points = {
                { x = 90.00,   y = 11585.00, z = 651.73 },
                { x = 1.00,    y = 10535.18, z = 806.97 },
                { x = -559.00, y = 11290.00, z = 601.00 }
            }
        },
        {
            task_patterns = {
                "\u{65C5}\u{9014}\u{4E4B}\u{59CB}",
                "\u{7A81}\u{7834}\u{89C9}\u{9192}\u{8005}\u{91CD}\u{56F4}",
                "\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}"
            },
            task_detail_patterns = {
                "\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}",
                "\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}"
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
            kite_radius = 1800,
            kite_switch_ms = 2200,
            seamless_kite = true,
            kite_arrive_distance = 420,
            kite_move_interval_ms = 120,
            boss_clear_settle_ms = 2500,
            generic_followup_refresh_ms = 3000,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true,
            kite_points = {
                { x = 1856.47, y = 4779.48, z = 1192.00 },
                { x = 2499.61, y = 5695.97, z = 1192.00 },
                { x = 3304.71, y = 5274.92, z = 1192.00 }
            }
        },
        {
            task_patterns = {
                "\u{9F99}\u{9668}\u{4E4B}\u{91CE}",
                "\u{6DF1}\u{5165}\u{9F99}\u{9AA8}\u{5C71}\u{810A}\u{8179}\u{5730}"
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

local function make_kingdom_end_deep_boss_task_config()
    return make_boss_kite_task_config(
        "kingdom_end_deep_boss_kite",
        {
            trigger_distance = 700,
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
            }
        },
        {
            task_patterns = {
                "\u{738B}\u{56FD}\u{7EC8}\u{9014}"
            },
            task_detail_patterns = {
                "\u{62B5}\u{8FBE}\u{56FD}\u{738B}\u{752C}\u{9053}\u{6700}\u{6DF1}\u{5904}",
                "\u{56FD}\u{738B}\u{752C}\u{9053}\u{6700}\u{6DF1}\u{5904}"
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

M.TASK_NAME_CONFIGS = {
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
        "dragonbone_plain_world_map_send",
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
            selection_step = {
                label = "\u{9F99}\u{9AA8}\u{5E73}\u{539F}\u{6309}\u{94AE}",
                distance_anchor_exact_text = "\u{9F99}\u{9AA8}\u{5E73}\u{539F}",
                distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn",
                distance_min = 128.003526,
                distance_max = 133.003526,
                include_patterns = {
                    "UIButton Transient.GameEngine.CoreGameInstance.WorldMapItem_C.WidgetTree.Btn"
                },
                hint_client_x = 695.675232,
                hint_client_y = 257.335999,
                hint_ratio_x = 0.483108,
                hint_ratio_y = 0.285929,
                hint_max_distance = 80.000
            },
            selection_settle_ms = 700
        },
        {
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
    ["\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}"] = make_dragonbone_griffin_boss_task_config(),
    ["\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}"] = make_dragonbone_griffin_boss_task_config(),
    ["\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"] = make_dragonbone_griffin_boss_task_config(),
    ["\u{5BFB}\u{627E}\u{77EE}\u{4EBA}\u{56FD}\u{5EA6}\u{5165}\u{53E3}"] = make_boss_kite_task_config(
        "plateau_dragonbone_beast_boss",
        {
            trigger_distance = 420,
            generic_followup_refresh_ms = 3500,
            generic_followup_requires_task_pos_only = true,
            generic_followup_require_no_special = true
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
    ["\u{51FB}\u{8D25}\u{7981}\u{533A}\u{5B88}\u{536B}"] = make_another_magic_academy_forbidden_guard_boss_task_config(),
    ["\u{5B88}\u{536B}\u{519B}\u{9886}\u{8896}\u{00B7}\u{963F}\u{5C14}\u{514B}\u{65AF}"] = make_another_magic_academy_forbidden_guard_boss_task_config(),
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
    ["\u{62B5}\u{8FBE}\u{56FD}\u{738B}\u{752C}\u{9053}\u{6700}\u{6DF1}\u{5904}"] = make_kingdom_end_deep_boss_task_config(),
    ["\u{56FD}\u{738B}\u{752C}\u{9053}\u{6700}\u{6DF1}\u{5904}"] = make_kingdom_end_deep_boss_task_config(),
    ["\u{51FB}\u{8D25}\u{7279}\u{6B8A}\u{5B9E}\u{9A8C}\u{4F53}\u{57FA}\u{5C14}"] = make_forgotten_temple_special_experiment_keel_task_config(),
    ["\u{7279}\u{6B8A}\u{5B9E}\u{9A8C}\u{4F53}\u{57FA}\u{5C14}"] = make_forgotten_temple_special_experiment_keel_task_config(),
    ["\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"] = make_abyss_below_awakened_temple_deep_route_task_config(),
    ["\u{8FDB}\u{5165}\u{89C9}\u{9192}\u{79D8}\u{6BBF}\u{6DF1}\u{5904}"] = make_abyss_below_awakened_temple_deep_route_task_config(),
    ["\u{9634}\u{4E91}\u{538B}\u{57CE}"] = make_overcast_city_guard_fire_seed_device_task_config(),
    ["\u{5B88}\u{62A4}\u{706B}\u{79CD}\u{88C5}\u{7F6E}"] = make_overcast_city_guard_fire_seed_device_task_config(),
    ["\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}"] = make_overcast_city_guard_fire_seed_device_task_config(),
    ["\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}"] = make_overcast_city_guard_fire_seed_device_task_config(),
    ["\u{65C5}\u{9014}\u{4E4B}\u{59CB}"] = make_journey_begin_awakened_leader_task_config(),
    ["\u{7A81}\u{7834}\u{89C9}\u{9192}\u{8005}\u{91CD}\u{56F4}"] = make_journey_begin_awakened_leader_task_config(),
    ["\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}"] = make_journey_begin_awakened_leader_task_config(),
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
            arm_task_entry_action_after_click = true,
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
        entry_action = {
            key = "day_of_apotheosis_enter_ascension_hall_world_map_send",
            mode = "world_map_send",
            map_open_wait_ms = 3000,
            center_click_ratio_x = 0.428472,
            center_click_ratio_y = 0.496667,
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
        objective = {
            key = "shadowland_fire_seed_gather",
            mode = "task_objective_button",
            trigger_distance = 320,
            skip_direct_interact = true,
            force_task_call_after_transition = true,
            task_pos_reject_extra_ms = 2500,
            ignore_terminal_text_change_when_objective_same = true,
            button_steps = {
                {
                    key = "shadowland_fire_seed_greatsword_gather",
                    label = "\u{7CBE}\u{94C1}\u{5DE8}\u{5251}\u{6309}\u{94AE}",
                    distance_anchor_exact_text = "\u{7CBE}\u{94C1}\u{5DE8}\u{5251}",
                    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
                    distance_min = 175.652132,
                    distance_max = 180.652132,
                    include_patterns = {
                        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn"
                    },
                    hint_client_x = 694.204834,
                    hint_client_y = 720.439941,
                    hint_ratio_x = 0.482087,
                    hint_ratio_y = 0.800489,
                    hint_max_distance = 80.000,
                    prefer_hint_fallback = true,
                    settle_ms = 1200
                }
            }
        },
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
            center_click_ratio_x = 0.503472,
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
                "\u{957F}\u{591C}\u{7EC8}\u{5C3D}",
                "\u{5723}\u{8BEC}\u{4E4B}\u{672B}"
            },
            task_detail_patterns = { "\u{524D}\u{5F80}\u{4F59}\u{70EC}\u{4E4B}\u{606F}" },
            main_task_call = {
                allow_anchor_click_fallback = true
            },
            constraint_mode = "all"
        }
    ),
    ["\u{5BFC}\u{5E08}\u{9988}\u{8D60}"] = make_post_dialogue_flow_task_config(
        "mentor_gift_task_detail_after_antonio",
        {
            make_fixed_client_click_step({
                key = "mentor_gift_task_detail_btn",
                label = "[\u{4EFB}\u{52A1}] \u{5BFC}\u{5E08}\u{9988}\u{8D60}\u{6309}\u{94AE}",
                fixed_client_x = 722.000000,
                fixed_client_y = 305.000000,
                fixed_ratio_x = 0.501389,
                fixed_ratio_y = 0.338889,
                settle_ms = 1200,
                force_task_call_after_transition = false,
                task_pos_reject_extra_ms = 2500
            })
        },
        {
            initial_delay_ms = 500,
            timeout_ms = 8000,
            arm_after_objective_button = true,
            skip_dialogue_jump = true
        },
        {
            task_patterns = { "\u{5BFC}\u{5E08}\u{9988}\u{8D60}" },
            task_detail_patterns = { "\u{4E0E}\u{5B89}\u{4E1C}\u{5C3C}\u{5965}\u{5B66}\u{8005}\u{5BF9}\u{8BDD}" },
            constraint_mode = "all"
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
    ["\u{4E0E}\u{83AB}\u{7433}\u{5A1C}\u{4EA4}\u{8C08}"] = make_post_dialogue_flow_task_config(
        "long_night_end_task_detail_after_molina",
        {
            make_fixed_client_click_step({
                key = "long_night_end_task_detail_btn",
                label = "[\u{4EFB}\u{52A1}] \u{957F}\u{591C}\u{7EC8}\u{5C3D}\u{6309}\u{94AE}",
                fixed_client_x = 766.000000,
                fixed_client_y = 281.000000,
                fixed_ratio_x = 0.531944,
                fixed_ratio_y = 0.312222,
                settle_ms = 1200,
                force_task_call_after_transition = false,
                task_pos_reject_extra_ms = 2500
            })
        },
        {
            initial_delay_ms = 500,
            timeout_ms = 8000,
            arm_after_objective_button = true,
            skip_dialogue_jump = true
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
}

M.TASK_NAME_CONFIGS["\u{5723}\u{8BEC}\u{4E4B}\u{672B}"] = make_world_map_send_task_config(
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
        center_click_ratio_x = 0.503472,
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
            "\u{5723}\u{8BEC}\u{4E4B}\u{672B}"
        },
        main_task_call = {
            allow_anchor_click_fallback = true
        }
    }
)

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
    Actions.make_lift_route_action({
        key = "lift_transition_2609_-3074",
        label = "电梯按钮",
        task_patterns = {
            "\u{963B}\u{6B62}\u{77EE}\u{4EBA}\u{738B}\u{7684}\u{9634}\u{8C0B}"
        },
        trigger = {
            x = 2609.02,
            y = -3074.28,
            z = -1695.90,
            radius = 260,
            z_tolerance = 260
        },
        retry_ms = 3500,
        settle_ms = 1200,
        force_task_call_after_transition = true,
        task_pos_reject_extra_ms = 2500,
        fallback_interact = true,
        fallback_interact_distance = 180,
        fallback_retry_ms = 2500,
        board = {
            x = -1471.00,
            y = 4002.00,
            z = 1121.00,
            radius = 220,
            interact_radius = 90,
            move_interval_ms = 200,
            z_tolerance = 220,
            allow_direct_entry = true,
            direct_entry_radius = 760,
            allow_direct_entry_without_task_match = true,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            settle_ms = 5000,
            timeout_ms = 22000
        },
        step = {
            label = "电梯按钮",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.LiftBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.LiftBtn"
            },
            hint_client_x = 694.204834,
            hint_client_y = 720.439941,
            hint_ratio_x = 0.482087,
            hint_ratio_y = 0.800489,
            hint_max_distance = 80.000,
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
        key = "listen_keli_dwarf_king_dialogue_8496_17639",
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
            x = 8496.49,
            y = 17639.27,
            z = 812.00,
            radius = 860,
            z_tolerance = 260
        },
        retry_ms = 6000,
        dialogue = {
            x = 8496.49,
            y = 17639.27,
            z = 812.00,
            radius = 260,
            interact_radius = 120,
            move_interval_ms = 180,
            z_tolerance = 220,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 420,
            fallback_interact = true
        }
    }),
    make_npc_dialogue_route_action({
        key = "keli_dialogue_11802_-7232",
        label = "keli_dialogue_11802_-7232_npc",
        task_patterns = {
            "\u{548C}\u{79D1}\u{91CC}\u{4EA4}\u{8C08}"
        },
        task_detail_patterns = {
            "\u{548C}\u{79D1}\u{91CC}\u{4EA4}\u{8C08}"
        },
        trigger = {
            x = 12351.00,
            y = -7100.00,
            radius = 180
        },
        retry_ms = 6000,
        dialogue = {
            x = 11802.00,
            y = -7232.00,
            z = 566.00,
            radius = 260,
            interact_radius = 160,
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
        key = "wall_of_sighs_aria_dialogue_18459_-2492",
        label = "wall_of_sighs_aria_dialogue_npc",
        task_patterns = {
            "\u{53F9}\u{606F}\u{4E4B}\u{5899}"
        },
        task_detail_patterns = {
            "\u{548C}\u{963F}\u{745E}\u{5A05}\u{4EA4}\u{8C08}"
        },
        constraint_mode = "all",
        trigger = {
            x = 18017.00,
            y = -2362.00,
            z = 403.00,
            radius = 2200,
            z_tolerance = 260
        },
        retry_ms = 6000,
        dialogue = {
            x = 18017.00,
            y = -2362.00,
            z = 403.00,
            radius = 260,
            interact_radius = 160,
            move_interval_ms = 180,
            z_tolerance = 260,
            center_settle_ms = 700,
            interact_retry_ms = 1800,
            timeout_ms = 22000,
            npc_search_radius = 560,
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
        retry_ms = 2500,
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
        key = "abyss_below_trace_ryan_anchor_3223_6136",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{8FFD}\u{5BFB}\u{83B1}\u{5B89}\u{8E2A}\u{8FF9}_\u{5165}\u{53E3}\u{951A}\u{70B9}_3223_6136",
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
            x = 3223.00,
            y = 6136.00,
            z = 503.00,
            radius = 2200,
            z_tolerance = 360
        },
        retry_ms = 600000,
        timeout_ms = 35000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 360,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = 3223.00, y = 6136.00, z = 503.00 }
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
        key = "abyss_below_principal_derek_dialogue_anchor_-1599_23620",
        label = "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}_\u{4E0E}\u{6821}\u{957F}\u{5FB7}\u{91CC}\u{514B}\u{5BF9}\u{8BDD}_\u{5165}\u{53E3}\u{951A}\u{70B9}",
        mode = "recorded_route_point",
        task_patterns = {
            "\u{6DF1}\u{6E0A}\u{4EE5}\u{4E0B}"
        },
        task_detail_patterns = {
            "\u{4E0E}\u{6821}\u{957F}\u{5FB7}\u{91CC}\u{514B}\u{5BF9}\u{8BDD}"
        },
        constraint_mode = "all",
        trigger = {
            x = -1599.00,
            y = 23620.00,
            z = 503.00,
            radius = 1500,
            z_tolerance = 320
        },
        retry_ms = 600000,
        timeout_ms = 30000,
        waypoint_reach_radius = 180,
        waypoint_z_tolerance = 320,
        move_interval_ms = 220,
        reacquire_retry_ms = 1200,
        waypoints = {
            { x = -1599.00, y = 23620.00, z = 503.00 }
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
    make_route_point_action({
        key = "counterattack_dawn_rescue_gather_19907_21521",
        label = "反击的黎明_解救被困者_交互按钮",
        mode = "objective_button_point",
        task_patterns = {
            "\u{53CD}\u{51FB}\u{7684}\u{9ECE}\u{660E}"
        },
        task_detail_patterns = {
            "\u{89E3}\u{6551}\u{88AB}\u{56F0}\u{8005}"
        },
        constraint_mode = "all",
        trigger = {
            x = 19907.18,
            y = 21521.48,
            z = 920.00,
            radius = 780,
            z_tolerance = 260
        },
        retry_ms = 2500,
        settle_ms = 1500,
        step = {
            key = "rescue_gather_btn",
            label = "\u{89E3}\u{6551}\u{88AB}\u{56F0}\u{8005}\u{6309}\u{94AE}",
            distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn",
            include_patterns = {
                "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.GatherBtn"
            },
            hint_client_x = 699.204834,
            hint_client_y = 724.439941,
            hint_ratio_x = 0.485559,
            hint_ratio_y = 0.804933,
            hint_max_distance = 80.000
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
        kite_radius = 1200,
        kite_switch_ms = 2400,
        seamless_kite = true,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        kite_points = {
            { x = 12803.00, y = -7308.00, z = 608.00 },
            { x = 11910.25, y = -6507.10, z = 607.00 },
            { x = 11391.75, y = -7504.81, z = 566.00 }
        },
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
        task_patterns = {
            "\u{7FA4}\u{5C71}\u{4E4B}\u{5FC3}",
            "\u{5B8C}\u{6210}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    }),
    Actions.make_clear_room_point({
        key = "mountain_heart_boss_room_7478_17145",
        x = 7477.54,
        y = 17144.58,
        z = 812.00,
        radius = 1100,
        trigger_distance = 820,
        kite_radius = 2880,
        kite_switch_ms = 2400,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 120,
        seamless_kite = true,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        kite_points = {
            { x = 5482.34, y = 17353.55, z = 811.00 },
            { x = 7245.18, y = 18406.44, z = 811.00 },
            { x = 8614.00, y = 17297.88, z = 811.00 },
            { x = 7309.67, y = 15983.46, z = 811.00 }
        },
        post_combat_loot = {
            enabled = true,
            duration_ms = 3500,
            max_duration_ms = 7000,
            press_interval_ms = 450,
            empty_settle_ms = 900
        },
        followup_route_action_key = "listen_keli_dwarf_king_dialogue_8496_17639",
        task_patterns = {
            "\u{7FA4}\u{5C71}\u{4E4B}\u{5FC3}",
            "\u{5B8C}\u{6210}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        },
        revive_reentry = make_revive_reentry_config({
            key = "mountain_heart_boss_room_reentry_4511_14022",
            label = "\u{7FA4}\u{5C71}\u{4E4B}\u{5FC3} Boss\u{91CD}\u{8FDB}\u{623F}",
            anchor = {
                x = 4511.00,
                y = 14022.00,
                z = 59.94,
                radius = 560
            },
            interact_distance = 280,
            retry_ms = 1200,
            settle_ms = 1400,
            timeout_ms = 20000,
            post_transition_boss_engage_ms = 16000
        })
    }),
    Actions.make_clear_room_point({
        key = "wasteland_path_longhorn_beast_room_25430_12440",
        x = 25430.00,
        y = 12440.00,
        z = 5448.75,
        radius = 1200,
        trigger_distance = 900,
        kite_radius = 3600,
        boss_clear_settle_ms = 3000,
        task_patterns = {
            "\u{707E}\u{5384}\u{5C06}\u{81F3}",
            "\u{9AD8}\u{539F}\u{957F}\u{89D2}\u{5F02}\u{517D}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{62E6}\u{8DEF}\u{7684}\u{957F}\u{89D2}\u{5F02}\u{517D}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        },
        revive_reentry = make_revive_reentry_config({
            key = "wasteland_path_longhorn_beast_room_reentry_24163_11202",
            label = "\u{8352}\u{829C}\u{5C71}\u{9053} Boss\u{91CD}\u{8FDB}\u{623F}",
            anchor = {
                x = 24163.00,
                y = 11202.00,
                z = 5446.06,
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
        kite_radius = 2200,
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
            { x = 15496.30, y = 22673.15, z = 1010.00 },
            { x = 16293.65, y = 22514.17, z = 1010.00 },
            { x = 15827.85, y = 21597.74, z = 1010.00 }
        },
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
            key = "old_dusk_lai_an_boss_room_reentry_14867_23340",
            label = "\u{65E7}\u{65E5}\u{7684}\u{9EC4}\u{660F} Boss\u{91CD}\u{8FDB}\u{623F}",
            anchor = {
                x = 14867.00,
                y = 23340.00,
                z = 1011.00,
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
        kite_radius = 1800,
        trigger_distance = 900,
        immediate_kite_on_reached = true,
        seamless_kite = true,
        kite_switch_ms = 2200,
        kite_arrive_distance = 420,
        kite_move_interval_ms = 120,
        boss_clear_settle_ms = 2500,
        generic_followup_refresh_ms = 3000,
        generic_followup_requires_task_pos_only = true,
        generic_followup_require_no_special = true,
        kite_points = {
            { x = 1856.47, y = 4779.48, z = 1192.00 },
            { x = 2499.61, y = 5695.97, z = 1192.00 },
            { x = 3304.71, y = 5274.92, z = 1192.00 }
        },
        task_patterns = {
            "\u{9F99}\u{9668}\u{4E4B}\u{91CE}",
            "\u{4E3B}\u{7EBF} \u{9F99}\u{9668}\u{4E4B}\u{91CE}",
            "\u{6DF1}\u{5165}\u{9F99}\u{9AA8}\u{5C71}\u{810A}\u{8179}\u{5730}",
            "\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"
        },
        task_detail_patterns = {
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
        kite_radius = 3600,
        trigger_distance = 900,
        allow_when_task_unknown = true,
        task_patterns = {
            "\u{5BFB}\u{627E}\u{77EE}\u{4EBA}\u{56FD}\u{5EA6}\u{5165}\u{53E3}",
            "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{5F02}\u{517D}",
            "\u{4E3B}\u{7EBF} \u{9F99}\u{9668}\u{4E4B}\u{91CE}"
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
        key = "free_fire_elsa_approval_boss_room",
        x = 17225.59,
        y = -6584.43,
        z = 606.00,
        radius = 1600,
        trigger_distance = 1000,
        kite_radius = 1800,
        kite_switch_ms = 2400,
        boss_clear_settle_ms = 3000,
        kite_points = {
            { x = 17723.00, y = -6545.00, z = 606.00 },
            { x = 16728.18, y = -6623.85, z = 606.00 },
            { x = 17723.00, y = -6545.00, z = 606.00 },
            { x = 16728.18, y = -6623.85, z = 606.00 }
        },
        task_patterns = {
            "\u{81EA}\u{7531}\u{7684}\u{7130}\u{706B}",
            "\u{53D6}\u{5F97}\u{4F0A}\u{5C14}\u{838E}\u{7684}\u{8BA4}\u{53EF}",
            "\u{94F6}\u{7130}\u{9996}\u{9886}\u{00B7}\u{4F0A}\u{5C14}\u{838E}"
        },
        task_detail_patterns = {
            "\u{53D6}\u{5F97}\u{4F0A}\u{5C14}\u{838E}\u{7684}\u{8BA4}\u{53EF}",
            "\u{94F6}\u{7130}\u{9996}\u{9886}\u{00B7}\u{4F0A}\u{5C14}\u{838E}"
        },
        constraint_mode = "all",
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    }),
    Actions.make_clear_room_point({
        key = "another_magic_academy_forbidden_guard_boss_room",
        x = 20700.00,
        y = 8610.00,
        z = 605.00,
        radius = 1400,
        trigger_distance = 900,
        immediate_kite_on_reached = true,
        kite_radius = 1800,
        kite_switch_ms = 2400,
        seamless_kite = true,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 120,
        defer_followup_until_clear = true,
        boss_clear_settle_ms = 3000,
        kite_points = {
            { x = 19680.23, y = 8403.38, z = 607.00 },
            { x = 20886.94, y = 7818.64, z = 605.00 },
            { x = 20750.76, y = 9283.95, z = 605.00 }
        },
        revive_reentry = make_another_magic_academy_forbidden_guard_revive_reentry_config(),
        task_patterns = {
            "\u{53E6}\u{4E00}\u{4E2A}\u{9B54}\u{6CD5}\u{5B66}\u{9662}",
            "\u{51FB}\u{8D25}\u{7981}\u{533A}\u{5B88}\u{536B}",
            "\u{5B88}\u{536B}\u{519B}\u{9886}\u{8896}\u{00B7}\u{963F}\u{5C14}\u{514B}\u{65AF}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{7981}\u{533A}\u{5B88}\u{536B}",
            "\u{5B88}\u{536B}\u{519B}\u{9886}\u{8896}\u{00B7}\u{963F}\u{5C14}\u{514B}\u{65AF}"
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
        key = "overcast_city_guard_fire_seed_device_kite",
        x = 5100.00,
        y = -600.00,
        z = 3100.00,
        radius = 1200,
        trigger_distance = 180,
        immediate_kite_on_reached = true,
        kite_radius = 1800,
        kite_switch_ms = 2400,
        seamless_kite = true,
        kite_arrive_distance = 520,
        kite_move_interval_ms = 180,
        boss_clear_settle_ms = 3500,
        kite_points = {
            { x = 5036.32, y = -177.00,  z = 3091.39 },
            { x = 4473.61, y = -1856.99, z = 3091.00 },
            { x = 5036.32, y = -177.00,  z = 3091.39 },
            { x = 4473.61, y = -1856.99, z = 3091.00 }
        },
        task_patterns = {
            "\u{9634}\u{4E91}\u{538B}\u{57CE}"
        },
        task_detail_patterns = {
            "\u{5B88}\u{62A4}\u{706B}\u{79CD}\u{88C5}\u{7F6E}",
            "\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}",
            "\u{89C9}\u{9192}\u{8005}\u{83B1}\u{5B89}"
        },
        constraint_mode = "all",
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    }),
    Actions.make_clear_room_point({
        key = "journey_begin_awakened_leader_kite",
        x = 90.00,
        y = 11585.00,
        z = 651.73,
        radius = 1200,
        trigger_distance = 180,
        immediate_kite_on_reached = true,
        kite_radius = 1800,
        kite_switch_ms = 1200,
        seamless_kite = true,
        kite_arrive_distance = 420,
        kite_move_interval_ms = 180,
        boss_clear_settle_ms = 3500,
        kite_points = {
            { x = 90.00,   y = 11585.00, z = 651.73 },
            { x = 1.00,    y = 10535.18, z = 806.97 },
            { x = -559.00, y = 11290.00, z = 601.00 }
        },
        task_patterns = {
            "\u{65C5}\u{9014}\u{4E4B}\u{59CB}",
            "\u{7A81}\u{7834}\u{89C9}\u{9192}\u{8005}\u{91CD}\u{56F4}",
            "\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}"
        },
        task_detail_patterns = {
            "\u{51FB}\u{8D25}\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}",
            "\u{89C9}\u{9192}\u{8005}\u{5934}\u{76EE}"
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

return M


