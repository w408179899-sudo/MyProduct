PROCESS_NAME = "torchlight_infinite.exe"
MODE = "driver"

ARRIVE_TOLERANCE = 120
OUTER_FINAL_SKIP_DISTANCE = 220
OUTSIDE_REFERENCE_MAX_DISTANCE = 1800
OUTSIDE_REFERENCE_MAX_Z_DIFF = 700
REPATH_INTERVAL_MS = 1500
POLL_INTERVAL_MS = 30
INIT_RETRY_MS = 3000
CALL_DELAY_MIN_MS = 500
CALL_DELAY_MAX_MS = 2000
ENTRY_FLOW_DELAY_MIN_MS = 300
ENTRY_FLOW_DELAY_MAX_MS = 1500
KEY_STAGE_DELAY_MS = 3000
MAP_ROUTE_ESCAPE_DELAY_MS = 2000
MAP_ROUTE_ESCAPE_HOLD_MS = 1500
STEP_RETRY_POLL_MS = 500
STEP_FETCH_TIMEOUT_MS = 15000
STEP_DEBUG_DUMP_AFTER_MS = 5000
STEP_WARN_INTERVAL_MS = 2000
ROUTE_START_RETRY_POLL_MS = 500
ROUTE_START_TIMEOUT_MS = 15000
ROUTE_START_WARN_INTERVAL_MS = 2000
MAP_ROUTE_READY_STABLE_MS = 1000

HOTKEY_START = 0x2D
HOTKEY_F3 = 0x72
HOTKEY_F4 = 0x73
HOTKEY_F5 = 0x74
HOTKEY_F6 = 0x75
HOTKEY_F7 = 0x76
HOTKEY_F8 = 0x77
HOTKEY_F9 = 0x78
HOTKEY_F10 = 0x79
HOTKEY_F11 = 0x7A
HOTKEY_EXIT_CTRL = 0x11
HOTKEY_EXIT = 0x7B
HOTKEY_START_BRACKET = 0xDB
HOTKEY_STOP_BRACKET = 0xDD
HOTKEY_F11_ALT = 0xDC
HOTKEY_F5_ENABLED = false
HOTKEY_F6_ENABLED = true
MYSTIC_MAP_DISTANCE_BUTTON_NAME = "UIButton Transient.GameEngine.CoreGameInstance.UIMysticMapItem_C.WidgetTree.ClickButton"
MYSTIC_MAP_DISTANCE_MIN = 114.35
MYSTIC_MAP_DISTANCE_MAX = 114.55
CONFIRM_DISTANCE_BUTTON_NAME = "UIButton Transient.GameEngine.CoreGameInstance.Confirm_C.WidgetTree.Button3.WidgetTree.ClickBtn"
CONFIRM_DISTANCE_MIN = 8.8
CONFIRM_DISTANCE_MAX = 9.8
RECYCLE_DISTANCE_BUTTON_NAME = "UIButton Transient.GameEngine.CoreGameInstance.PCBag_C.WidgetTree.PCBagMain.WidgetTree.PCUIGridListView.WidgetTree.UIButton_Recycle"
RECYCLE_DISTANCE_MIN = 21.8
RECYCLE_DISTANCE_MAX = 23.8
FENJIE_SUOYOU_BUTTON_NAME = "UIButton Transient.GameEngine.CoreGameInstance.PCBag_C.WidgetTree.PCBagMain.WidgetTree.PCUIGridListView.WidgetTree.PCBagFilterRarityItem.WidgetTree.SelectBtn0"
HOTKEY_BUTTON_ENUM_PRESETS = {
    current = {
        label = "当前鼠标按钮定位分析",
        button_name = "",
        limit = 8,
        include_zero_position = false,
        nearest_text_max_distance = 260,
        cursor_max_distance = 30
    }
}
HOTKEY_CURSOR_CLICK_PRESETS = {
    current = {
        label = "鼠标按钮验证点击",
        button_name = "",
        limit = 3,
        include_zero_position = false,
        nearest_text_max_distance = 260,
        cursor_max_distance = 2,
        distance_tolerance_min = 0.5,
        distance_tolerance_max = 2.5,
        distance_tolerance_ratio = 0.03,
        hint_max_distance = 80
    }
}
HOTKEY_DISTANCE_PREVIEW_PRESETS = {
    current = {
        label = "分解所有按钮",
        include_patterns = {
            FENJIE_SUOYOU_BUTTON_NAME
        }
    }
}
HOTKEY_DISTANCE_CLICK_PRESETS = {
    current = {
        label = "回旋矿场",
        anchor_exact_text = "回旋矿场",
        button_name = MYSTIC_MAP_DISTANCE_BUTTON_NAME,
        distance_min = MYSTIC_MAP_DISTANCE_MIN,
        distance_max = MYSTIC_MAP_DISTANCE_MAX
    }
}
TASK_MODE = {
    GOLD = 1,
    LEVELING = 2,
    OPEN_MAP = 3,
    DEFAULT = 1,
    CONFIG_KEY = "avepointTaskMode",
    LABELS = {
        [1] = "刷金",
        [2] = "练级",
        [3] = "开图"
    },
    runner = nil
}
VK_A = 0x41
VK_B = 0x42
VK_D = 0x44
VK_T = 0x54
VK_ESCAPE = 0x1B
PICKUP_SCAN_INTERVAL_MS = 250
PICKUP_PRESS_INTERVAL_MS = 700
PICKUP_INFO_INTERVAL_MS = 2000
PICKUP_WARN_INTERVAL_MS = 3000
PICKUP_STUCK_MAX_ATTEMPTS = 50
PICKUP_BAG_FULL_MIN_ITEMS = 2
BAG_FLOW_DELAY_MIN_MS = 200
BAG_FLOW_DELAY_MAX_MS = 1000
BAG_CLEANUP_POLL_MS = 500
BAG_CLEANUP_TIMEOUT_MS = 15000
BAG_CLEANUP_WARN_INTERVAL_MS = 2000
BAG_CLEANUP_KEY_DELAY_MS = 700
BAG_CLEANUP_CLICK_DELAY_MS = 700
BAG_CLEANUP_CONFIRM_DELAY_MS = 1000
BAG_CLEANUP_RUNS_MIN = 10
BAG_CLEANUP_RUNS_MAX = 20
STARTUP_RECOVER_T_WAIT_MIN_MS = 4000
STARTUP_RECOVER_T_WAIT_MAX_MS = 5000
HUMAN_IDLE_MOVE_MIN_INTERVAL_MS = 5000
HUMAN_IDLE_MOVE_MAX_INTERVAL_MS = 15000
HUMAN_MOUSE_MOVE_DURATION = {
    min_ms = 300,
    max_ms = 800,
    center_ms = 460,
    sigma_ms = 95,
    gaussian_weight = 0.88,
    report_rate_hz = 36
}
IMAGE_CLICK_RETRY_VERIFY_DELAY_MS = 500
IMAGE_CLICK_RETRY_VERIFY_TIMEOUT_MS = 2500
IMAGE_CLICK_RETRY_POLL_MS = 350
IMAGE_CLICK_RETRY_POSITION_TOLERANCE = 24
IMAGE_CLICK_RETRY_COMPARE_THRESHOLD = 0.995
IMAGE_CLICK_RETRY_STAGE_DELAY_MS = 500
EXIT_VERIFY_RETRY_MS = 500
EXIT_STILL_IN_MAP_DISTANCE = 500
EXIT_UNSTUCK_MOVE_DISTANCE = 520
EXIT_UNSTUCK_ESCAPE_DELAY_MS = 500
F6_LOOP_TOTAL_ROUNDS = 3
F6_LOOP_MAP_DURATION_MS = 5 * 60 * 1000
F6_LOOP_RELAUNCH_DELAY_MS = 60 * 1000
MAP_ROUTE_STUCK_SKIP_MS = 10000
MAP_ROUTE_STUCK_PROGRESS_RESET_DISTANCE = 80
MAP_ROUTE_STUCK_RIGHT_CLICK_RETRY_MS = 1800
ZHONGJI_WANGZUO_PORTAL_MAX_ATTEMPTS = 2

BAG_CLEANUP_STEP_RECYCLE_OPEN = "Bag Cleanup Recycle Open"
BAG_CLEANUP_STEP_FENJIE_SUOYOU = "Bag Cleanup Fenjie Suoyou"
BAG_CLEANUP_STEP_RECYCLE_CONFIRM = "Bag Cleanup Recycle Confirm"
BAG_CLEANUP_STEP_RECYCLE_FINAL_CONFIRM = "Bag Cleanup Recycle Final Confirm"
STASH_STEP_ONECLICK_STORE = "Stash OneClick Store"
STASH_STEP_BACK = "Stash Back"
EXIT_STEP_PORTAL = "Exit Portal Button"
EXIT_STEP_CHUMEN = "Exit Chumen"

ROUTE_POINTS = {
    { x = 1506.00, y = -2421.00, z = 3136.09 },
    { x = 1773.00, y = 831.00, z = 3092.00 },
    { x = 4175.00, y = 2885.00, z = 3116.41 },
    { x = 4945.27, y = 3439.51, z = 3296.01 },
    { x = 5649.00, y = 4562.00, z = 3604.81 }
}

STASH_ROUTE_POINTS = {
    { x = 5638.00, y = 4467.00, z = 3601.58 },
    { x = 4222.90, y = 2614.18, z = 3116.78 },
    { x = 4509.01, y = 2057.58, z = 3096.90 }
}

STASH_RETURN_ROUTE_POINTS = {
    { x = 4509.01, y = 2057.58, z = 3096.90 },
    { x = 4222.90, y = 2614.18, z = 3116.78 },
    { x = 5638.00, y = 4467.00, z = 3601.58 },
    { x = 5649.00, y = 4562.00, z = 3604.81 }
}

OUTSIDE_REFERENCE_POINTS = {}

function append_outside_reference_points(points)
    if type(points) ~= "table" then
        return
    end

    for _, point in ipairs(points) do
        if type(point) == "table"
            and tonumber(point.x) ~= nil
            and tonumber(point.y) ~= nil
        then
            OUTSIDE_REFERENCE_POINTS[#OUTSIDE_REFERENCE_POINTS + 1] = {
                x = tonumber(point.x),
                y = tonumber(point.y),
                z = tonumber(point.z)
            }
        end
    end
end

append_outside_reference_points(ROUTE_POINTS)
append_outside_reference_points(STASH_ROUTE_POINTS)
append_outside_reference_points(STASH_RETURN_ROUTE_POINTS)

function make_mystic_area_step()
    return {
        label = "MysticArea ClickButton",
        include_patterns = {
            "uimysticareaitem_c.widgettree.clickbutton",
            "uimysticareaitem_c",
            "clickbutton"
        },
        related_exact_texts = {
            "冰封寒渊"
        },
        related_max_distance = 120
    }
end

function make_common_next_step()
    return {
        label = "MysteryMapDetail OpenBtn",
        include_patterns = {
            "mysterymapdetail_c.widgettree.openbtn",
            "openbtn"
        },
        exclude_patterns = {
            "mysterybossdetail_c.widgettree.enterbtn"
        }
    }
end

function make_mystic_map_distance_step(map_label)
    return {
        label = "MysticMap ClickButton",
        distance_anchor_exact_text = tostring(map_label or ""),
        distance_button_name = MYSTIC_MAP_DISTANCE_BUTTON_NAME,
        distance_min = MYSTIC_MAP_DISTANCE_MIN,
        distance_max = MYSTIC_MAP_DISTANCE_MAX
    }
end

function make_select_ditu_confirm_step()
    return {
        label = "SelectDituConfirm",
        distance_anchor_exact_text = "确认",
        distance_button_name = CONFIRM_DISTANCE_BUTTON_NAME,
        distance_min = CONFIRM_DISTANCE_MIN,
        distance_max = CONFIRM_DISTANCE_MAX
    }
end

function make_bag_cleanup_recycle_open_step()
    return {
        label = BAG_CLEANUP_STEP_RECYCLE_OPEN,
        distance_anchor_exact_text = "回收",
        distance_button_name = RECYCLE_DISTANCE_BUTTON_NAME,
        distance_min = RECYCLE_DISTANCE_MIN,
        distance_max = RECYCLE_DISTANCE_MAX
    }
end

function make_bag_cleanup_recycle_confirm_step()
    return {
        label = BAG_CLEANUP_STEP_RECYCLE_CONFIRM,
        distance_anchor_exact_text = "回收",
        distance_button_name = RECYCLE_DISTANCE_BUTTON_NAME,
        distance_min = 21.990446,
        distance_max = 23.350679,
        include_patterns = {
            RECYCLE_DISTANCE_BUTTON_NAME
        },
        hint_client_x = 1360.144043,
        hint_client_y = 813.371216,
        hint_ratio_x = 0.944544,
        hint_ratio_y = 0.903746,
        hint_max_distance = 80.000
    }
end

function make_bag_cleanup_recycle_final_confirm_step()
    return {
        label = BAG_CLEANUP_STEP_RECYCLE_FINAL_CONFIRM,
        distance_anchor_exact_text = "确认",
        distance_button_name = CONFIRM_DISTANCE_BUTTON_NAME,
        distance_min = 8.766971,
        distance_max = 9.766971,
        include_patterns = {
            CONFIRM_DISTANCE_BUTTON_NAME
        },
        hint_client_x = 720.668884,
        hint_client_y = 560.391968,
        hint_ratio_x = 0.500465,
        hint_ratio_y = 0.622658,
        hint_max_distance = 80.000
    }
end

function make_revive_at_checkpoint_step()
    return {
        label = "ReviveAtCheckpoint",
        distance_anchor_exact_text = "记录点复活",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightFailure_C.WidgetTree.InsideRebornBtn.WidgetTree.ClickBtn",
        distance_min = 8.661942,
        distance_max = 9.661942,
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FightFailure_C.WidgetTree.InsideRebornBtn.WidgetTree.ClickBtn"
        },
        hint_client_x = 567.913635,
        hint_client_y = 838.957703,
        hint_ratio_x = 0.394384,
        hint_ratio_y = 0.932175,
        hint_max_distance = 80.000
    }
end

function make_revive_at_town_step()
    return {
        label = "ReviveAtTown",
        distance_anchor_exact_text = "城镇复活",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightFailure_C.WidgetTree.TownRebornBtn.WidgetTree.ClickBtn",
        distance_min = 8.661942,
        distance_max = 9.661942,
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FightFailure_C.WidgetTree.TownRebornBtn.WidgetTree.ClickBtn"
        },
        hint_client_x = 734.492798,
        hint_client_y = 838.957703,
        hint_ratio_x = 0.510064,
        hint_ratio_y = 0.932175,
        hint_max_distance = 80.000
    }
end

function make_bag_cleanup_fenjie_suoyou_step()
    return {
        label = BAG_CLEANUP_STEP_FENJIE_SUOYOU,
        distance_anchor_exact_text = "所有品质",
        distance_button_name = FENJIE_SUOYOU_BUTTON_NAME,
        distance_min = 11.005800,
        distance_max = 12.005800,
        include_patterns = {
            FENJIE_SUOYOU_BUTTON_NAME
        },
        hint_client_x = 967.542480,
        hint_client_y = 813.371216,
        hint_ratio_x = 0.671905,
        hint_ratio_y = 0.903746,
        hint_max_distance = 80.000
    }
end

function make_stash_oneclick_store_step()
    return {
        label = STASH_STEP_ONECLICK_STORE,
        distance_anchor_exact_text = "一键存入",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WareHouse_C.WidgetTree.DepositBtn",
        distance_min = 8.767026,
        distance_max = 9.767026,
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.WareHouse_C.WidgetTree.DepositBtn"
        },
        hint_client_x = 1052.790405,
        hint_client_y = 678.241577,
        hint_ratio_x = 0.731104,
        hint_ratio_y = 0.753602,
        hint_max_distance = 80.000
    }
end

function make_stash_back_step()
    return {
        label = STASH_STEP_BACK,
        distance_anchor_exact_text = "60FPS",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.WareHouse_C.WidgetTree.UITitleItem.WidgetTree.BackBtn",
        distance_min = 20.176952,
        distance_max = 21.425010,
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.WareHouse_C.WidgetTree.UITitleItem.WidgetTree.BackBtn"
        },
        hint_client_x = 23.823997,
        hint_client_y = 43.000000,
        hint_ratio_x = 0.016544,
        hint_ratio_y = 0.047778,
        hint_max_distance = 80.000
    }
end

function make_exit_portal_step()
    return {
        label = EXIT_STEP_PORTAL,
        distance_anchor_exact_text = "珍贵灰烬x2",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
        distance_min = 138.422655,
        distance_max = 143.422655,
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
        },
        hint_client_x = 700.204834,
        hint_client_y = 730.439941,
        hint_ratio_x = 0.486253,
        hint_ratio_y = 0.811600,
        hint_max_distance = 80.000
    }
end

function make_common_portal_step()
    return {
        label = "MysteryArea CommonCardBtn",
        include_patterns = {
            "mysteryarea_c.widgettree.commoncardbtn",
            "commoncardbtn",
            "开启传送门"
        },
        exclude_patterns = {
            "mysterybossdetail_c.widgettree.enterbtn",
            "mysterymapdetail_c.widgettree.openbtn"
        },
        related_exact_texts = {
            "开启传送门"
        },
        related_max_distance = 80,
        after_click_delay_ms = 8000
    }
end

function make_switch_random_map_entry_confirm_policy(options)
    local opts = type(options) == "table" and options or {}
    return {
        kind = "switch_random_map",
        retry_limit = math.max(1, math.floor(tonumber(opts.retry_limit) or 2)),
        retry_counter_step_label = tostring(opts.retry_counter_step_label or ""),
        retry_counter_log_label = tostring(opts.retry_counter_log_label or "入口触发点击"),
        exhausted_log_label = tostring(opts.exhausted_log_label or "当前地图"),
        close_before_switch_label = tostring(opts.close_before_switch_label or "close unavailable portal page"),
        switch_reason = tostring(opts.switch_reason or "entry_confirm_retry_limit"),
        exclude_current_map = opts.exclude_current_map ~= false,
        retry_stage = tostring(opts.retry_stage or "entry_buttons"),
        retry_stage_delay_ms = tonumber(opts.retry_stage_delay_ms)
    }
end

MAP_CONFIGS = {
    zhongji_wangzuo = {
        label = "终极王座",
        route_file = "map/zhongji_wangzuo.txt",
        exit_point = { x = 774.00, y = 1159.00, z = 41.00 },
        entry_key_vk = VK_D,
        entry_key_delay_ms = KEY_STAGE_DELAY_MS,
        exit_key_vk = VK_D,
        exit_key_delay_ms = KEY_STAGE_DELAY_MS,
        reenter_key_vk = VK_D,
        reenter_key_delay_ms = KEY_STAGE_DELAY_MS,
        map_route_escape_enabled = true,
        map_route_escape_delay_ms = MAP_ROUTE_ESCAPE_DELAY_MS,
        map_route_escape_hold_ms = MAP_ROUTE_ESCAPE_HOLD_MS,
        entry_confirm_policy = make_switch_random_map_entry_confirm_policy({
            retry_limit = ZHONGJI_WANGZUO_PORTAL_MAX_ATTEMPTS,
            retry_counter_step_label = "MysteryBossDetail EnterBtn",
            retry_counter_log_label = "终极王座开启传送门点击",
            exhausted_log_label = "终极王座",
            close_before_switch_label = "close unavailable zhongji_wangzuo portal page",
            switch_reason = "zhongji_wangzuo_no_ticket"
        }),
        entry_button_steps = {
            make_mystic_area_step(),
            {
                label = "MysticMap ClickButton",
                include_patterns = {
                    "uimysticmapitem_c.widgettree.clickbutton"
                },
                exclude_patterns = {
                    "uimysticareaitem_c.widgettree.clickbutton",
                    "mysterycardview.widgettree.clickbtn"
                },
                related_exact_texts = {
                    "沸涌炎海"
                },
                related_max_distance = 120
            },
            {
                label = "MysteryBossDetail EnterBtn",
                prefer_screen_click = true,
                include_patterns = {
                    "mysterybossdetail_c.widgettree.enterbtn",
                    "enterbtn",
                    "开启传送门"
                },
                exclude_patterns = {
                    "mysteryarea_c.widgettree.commoncardbtn",
                    "mysterymapdetail_c.widgettree.openbtn"
                },
                related_exact_texts = {
                    "开启传送门"
                },
                related_max_distance = 80,
                after_click_delay_ms = 8000
            }
        }
    },
    zawu_jiequ = {
        label = "杂芜街区",
        route_file = "map/zawu_jiequ.txt",
        exit_point = { x = 16799.58, y = -12075.07, z = 105.00 },
        entry_key_vk = VK_D,
        entry_key_delay_ms = KEY_STAGE_DELAY_MS,
        exit_key_vk = VK_D,
        exit_key_delay_ms = KEY_STAGE_DELAY_MS,
        reenter_key_vk = VK_D,
        reenter_key_delay_ms = KEY_STAGE_DELAY_MS,
        map_route_escape_enabled = false,
        entry_button_steps = {
            make_mystic_area_step(),
            make_mystic_map_distance_step("杂芜街区"),
            make_common_next_step(),
            {
                label = "MysteryMapDetail OpenBtn Final",
                include_patterns = {
                    "mysterymapdetail_c.widgettree.openbtn",
                    "openbtn"
                },
                exclude_patterns = {
                    "mysterybossdetail_c.widgettree.enterbtn",
                    "mysteryarea_c.widgettree.commoncardbtn"
                },
                related_exact_texts = {
                    "1"
                },
                related_include_patterns = {
                    "requirecosticon.widgettree.uuitextblock_num"
                },
                related_max_distance = 80,
                after_click_delay_ms = 8000
            }
        }
    },
    zhongxi_gaoqiang = {
        label = "终息高墙",
        route_file = "map/zhongxi_gaoqiang.txt",
        exit_point = { x = -7819.00, y = 7521.00, z = -2976.94 },
        entry_key_vk = VK_D,
        entry_key_delay_ms = KEY_STAGE_DELAY_MS,
        exit_key_vk = VK_D,
        exit_key_delay_ms = KEY_STAGE_DELAY_MS,
        reenter_key_vk = VK_D,
        reenter_key_delay_ms = KEY_STAGE_DELAY_MS,
        map_route_escape_enabled = false,
        map_route_interact_points = {
            {
                index = 23,
                x = -3674.84,
                y = 22387.41,
                arrive_tolerance = 220,
                key_vk = VK_D,
                after_key_delay_ms = 3000,
                label = "Map teleport floor 2"
            }
        },
        entry_button_steps = {
            make_mystic_area_step(),
            make_mystic_map_distance_step("终息高墙"),
            make_common_next_step(),
            {
                label = "MysteryMapDetail OpenBtn Final",
                include_patterns = {
                    "mysterymapdetail_c.widgettree.openbtn",
                    "openbtn"
                },
                exclude_patterns = {
                    "mysterybossdetail_c.widgettree.enterbtn",
                    "mysteryarea_c.widgettree.commoncardbtn"
                },
                related_exact_texts = {
                    "1"
                },
                related_include_patterns = {
                    "requirecosticon.widgettree.uuitextblock_num"
                },
                related_max_distance = 80,
                after_click_delay_ms = 8000
            }
        }
    },
    huixuan_kuangchang = {
        label = "回旋矿场",
        route_file = "map/huixuan_kuangchang.txt",
        exit_point = { x = -2722.00, y = 7862.00, z = 183.00 },
        entry_key_vk = VK_D,
        entry_key_delay_ms = KEY_STAGE_DELAY_MS,
        exit_key_vk = VK_D,
        exit_key_delay_ms = KEY_STAGE_DELAY_MS,
        reenter_key_vk = VK_D,
        reenter_key_delay_ms = KEY_STAGE_DELAY_MS,
        map_route_escape_enabled = false,
        entry_button_steps = {
            make_mystic_area_step(),
            make_mystic_map_distance_step("回旋矿场"),
            make_common_next_step(),
            {
                label = "MysteryMapDetail OpenBtn Final",
                include_patterns = {
                    "mysterymapdetail_c.widgettree.openbtn",
                    "openbtn"
                },
                exclude_patterns = {
                    "mysterybossdetail_c.widgettree.enterbtn",
                    "mysteryarea_c.widgettree.commoncardbtn"
                },
                related_exact_texts = {
                    "1"
                },
                related_include_patterns = {
                    "requirecosticon.widgettree.uuitextblock_num"
                },
                related_max_distance = 80,
                after_click_delay_ms = 8000
            }
        }
    },
    huangqi_kuangchang = {
        label = "荒弃矿场",
        route_file = "map/huangqi_kuangchang.txt",
        exit_point = { x = 13689.86, y = 5508.90, z = -2105.15 },
        entry_key_vk = VK_D,
        entry_key_delay_ms = KEY_STAGE_DELAY_MS,
        exit_key_vk = VK_D,
        exit_key_delay_ms = KEY_STAGE_DELAY_MS,
        reenter_key_vk = VK_D,
        reenter_key_delay_ms = KEY_STAGE_DELAY_MS,
        map_route_escape_enabled = false,
        entry_button_steps = {
            make_mystic_area_step(),
            make_mystic_map_distance_step("荒弃矿场"),
            make_common_next_step(),
            {
                label = "MysteryMapDetail OpenBtn Final",
                include_patterns = {
                    "mysterymapdetail_c.widgettree.openbtn",
                    "openbtn"
                },
                exclude_patterns = {
                    "mysterybossdetail_c.widgettree.enterbtn",
                    "mysteryarea_c.widgettree.commoncardbtn"
                },
                related_exact_texts = {
                    "1"
                },
                related_include_patterns = {
                    "requirecosticon.widgettree.uuitextblock_num"
                },
                related_max_distance = 80,
                after_click_delay_ms = 8000
            }
        }
    },
    mingsha_cunluo = {
        label = "鸣沙村落",
        route_file = "map/mingsha_cunluo.txt",
        exit_point = { x = 28145.00, y = 22372.00, z = 1662.00 },
        entry_key_vk = VK_D,
        entry_key_delay_ms = KEY_STAGE_DELAY_MS,
        exit_key_vk = VK_D,
        exit_key_delay_ms = KEY_STAGE_DELAY_MS,
        reenter_key_vk = VK_D,
        reenter_key_delay_ms = KEY_STAGE_DELAY_MS,
        map_route_escape_enabled = false,
        entry_button_steps = {
            make_mystic_area_step(),
            make_mystic_map_distance_step("鸣沙村落"),
            make_common_next_step(),
            {
                label = "MysteryMapDetail OpenBtn Final",
                include_patterns = {
                    "mysterymapdetail_c.widgettree.openbtn",
                    "openbtn"
                },
                exclude_patterns = {
                    "mysterybossdetail_c.widgettree.enterbtn",
                    "mysteryarea_c.widgettree.commoncardbtn"
                },
                related_exact_texts = {
                    "1"
                },
                related_include_patterns = {
                    "requirecosticon.widgettree.uuitextblock_num"
                },
                related_max_distance = 80,
                after_click_delay_ms = 8000
            }
        }
    },
    yaren_cunluo = {
        label = "亚人村落",
        route_file = "map/yaren_cunluo.txt",
        exit_point = { x = 13247.40, y = -4246.11, z = 904.85 },
        entry_key_vk = VK_D,
        entry_key_delay_ms = KEY_STAGE_DELAY_MS,
        exit_key_vk = VK_D,
        exit_key_delay_ms = KEY_STAGE_DELAY_MS,
        reenter_key_vk = VK_D,
        reenter_key_delay_ms = KEY_STAGE_DELAY_MS,
        map_route_escape_enabled = false,
        entry_button_steps = {
            make_mystic_area_step(),
            make_mystic_map_distance_step("亚人村落"),
            make_common_next_step(),
            {
                label = "MysteryMapDetail OpenBtn Final",
                include_patterns = {
                    "mysterymapdetail_c.widgettree.openbtn",
                    "openbtn"
                },
                exclude_patterns = {
                    "mysterybossdetail_c.widgettree.enterbtn",
                    "mysteryarea_c.widgettree.commoncardbtn"
                },
                related_exact_texts = {
                    "1"
                },
                related_include_patterns = {
                    "requirecosticon.widgettree.uuitextblock_num"
                },
                related_max_distance = 80,
                after_click_delay_ms = 8000
            }
        }
    }
}

RANDOM_MAP_POOL_KEYS = {
    "zhongji_wangzuo",
    "zawu_jiequ",
    "zhongxi_gaoqiang",
    "huixuan_kuangchang",
    "huangqi_kuangchang",
    "mingsha_cunluo",
    "yaren_cunluo"
}

for _, map_key in ipairs(RANDOM_MAP_POOL_KEYS) do
    if not MAP_CONFIGS[map_key] then
        error("Unknown random map config: " .. tostring(map_key))
    end
end

current_map_key = nil
current_map = nil

IMAGE_CLICK_PRESETS = {
    [EXIT_STEP_CHUMEN] = {
        template_path = "Ha/chumen.bmp",
        template_threshold = 0.99,
        click_button = "left",
        click_delay = 50,
        click_mode = "api",
        hover_delay_ms = 80,
        click_center = true,
        click_offset_x = 0,
        click_offset_y = 0,
        capture_set_foreground = true,
        capture_foreground_delay_ms = 60
    },
    ExitGame = {
        template_path = "Ha/exit_game.bmp",
        template_threshold = 0.99,
        click_button = "left",
        click_delay = 50,
        click_mode = "api",
        hover_delay_ms = 80,
        click_center = true,
        click_offset_x = 0,
        click_offset_y = 0,
        retry_until_target_disappears = true,
        retry_verify_delay_ms = IMAGE_CLICK_RETRY_VERIFY_DELAY_MS,
        retry_verify_timeout_ms = IMAGE_CLICK_RETRY_VERIFY_TIMEOUT_MS,
        retry_verify_poll_ms = IMAGE_CLICK_RETRY_POLL_MS,
        retry_same_target_distance = IMAGE_CLICK_RETRY_POSITION_TOLERANCE,
        retry_compare_threshold = IMAGE_CLICK_RETRY_COMPARE_THRESHOLD,
        capture_set_foreground = true,
        capture_foreground_delay_ms = 60
    },
    ExitGameConfirm = {
        template_path = "Ha/exit_game_confirm.bmp",
        template_threshold = 0.99,
        click_button = "left",
        click_delay = 50,
        click_mode = "api",
        hover_delay_ms = 80,
        click_center = true,
        click_offset_x = 0,
        click_offset_y = 0,
        capture_set_foreground = true,
        capture_foreground_delay_ms = 60
    },
    ClickStartGame = {
        template_path = "Ha/click_startGame.bmp",
        template_threshold = 0.99,
        click_button = "left",
        click_delay = 50,
        click_mode = "api",
        hover_delay_ms = 80,
        click_center = true,
        click_offset_x = 0,
        click_offset_y = 0,
        capture_set_foreground = true,
        capture_foreground_delay_ms = 60
    },
    StartGame = {
        template_path = "Ha/start_game.bmp",
        template_threshold = 0.99,
        click_button = "left",
        click_delay = 50,
        click_mode = "api",
        hover_delay_ms = 80,
        click_center = true,
        click_offset_x = 0,
        click_offset_y = 0,
        capture_set_foreground = true,
        capture_foreground_delay_ms = 60
    }
}

local function shallow_copy_table(tbl)
    if type(tbl) ~= "table" then
        return nil
    end

    local copy = {}
    for key, value in pairs(tbl) do
        copy[key] = value
    end
    return copy
end

function resolve_image_click_preset(step)
    if type(step) ~= "table" then
        return nil
    end

    if type(step.image_preset) == "table" then
        return shallow_copy_table(step.image_preset)
    end

    local step_label = tostring(step.label or "")
    return shallow_copy_table(IMAGE_CLICK_PRESETS[step_label])
end

BAG_CLEANUP_ACTIONS = {
    {
        kind = "key",
        vk = VK_B,
        label = "open bag for cleanup",
        after_delay_ms = BAG_CLEANUP_KEY_DELAY_MS
    },
    {
        kind = "image",
        step = make_bag_cleanup_recycle_open_step(),
        after_delay_ms = BAG_CLEANUP_CLICK_DELAY_MS
    },
    {
        kind = "image",
        step = make_bag_cleanup_fenjie_suoyou_step(),
        after_delay_ms = BAG_CLEANUP_CLICK_DELAY_MS
    },
    {
        kind = "image",
        step = make_bag_cleanup_recycle_confirm_step(),
        after_delay_ms = BAG_CLEANUP_CLICK_DELAY_MS
    },
    {
        kind = "image",
        step = make_bag_cleanup_recycle_final_confirm_step(),
        after_delay_ms = BAG_CLEANUP_CLICK_DELAY_MS
    },
    {
        kind = "relative_click",
        label = "Bag Cleanup Dismiss Area",
        base = "last_click",
        offset_x_min = 100,
        offset_x_max = 150,
        offset_y_min = 100,
        offset_y_max = 150,
        click_button = "left",
        click_delay = 50,
        click_mode = "api",
        hover_delay_ms = 80,
        after_delay_ms = BAG_CLEANUP_CONFIRM_DELAY_MS
    },
    {
        kind = "key",
        vk = VK_ESCAPE,
        label = "close bag after cleanup by escape",
        after_delay_ms = BAG_CLEANUP_KEY_DELAY_MS
    }
}

guard = {
    protect_process = true,
    key_file = "key.txt",
    engine_config = "config.json"
}

state = {
    running = false,
    task_mode_id = nil,
    task_mode_name = nil,
    stage = "idle",
    wait_until = 0,
    route = nil,
    map_points = nil,
    current_map_key = nil,
    current_map_label = nil,
    cycle_index = 0,
    cleanup_runs_completed = 0,
    cleanup_runs_target = 0,
    bag_cleanup_index = 1,
    bag_cleanup_next_stage = nil,
    bag_cleanup_retry_index = nil,
    bag_cleanup_retry_started_at = 0,
    bag_cleanup_retry_last_warn_at = 0,
    bag_cleanup_last_click_screen_x = nil,
    bag_cleanup_last_click_screen_y = nil,
    bag_cleanup_last_click_hwnd = nil,
    stash_next_stage = nil,
    stash_retry_started_at = 0,
    stash_retry_last_warn_at = 0,
    exit_image_retry_started_at = 0,
    exit_image_retry_last_warn_at = 0,
    button_index = 1,
    button_retry_index = nil,
    button_retry_started_at = 0,
    button_retry_last_warn_at = 0,
    button_retry_dumped = false,
    entry_portal_started_at = 0,
    entry_portal_last_warn_at = 0,
    entry_portal_ready_at = 0,
    entry_portal_retry_due_at = 0,
    entry_portal_click_attempts = 0,
    route_escape_due_at = 0,
    route_escape_sent = false,
    route_escape_hold_until = 0,
    route_start_key = nil,
    route_start_started_at = 0,
    route_start_last_warn_at = 0,
    route_start_ready_at = 0,
    last_clicked_entry_label = nil,
    pickup_active = false,
    pickup_next_at = 0,
    pickup_last_warn_at = 0,
    pickup_last_info_at = 0,
    pickup_last_seen_count = 0,
    pickup_last_logged_count = 0,
    pickup_stuck_reference_count = 0,
    pickup_stuck_attempts = 0,
    pickup_skip_until_exit = false,
    force_cleanup_after_exit = false,
    exit_verify_started_at = 0,
    exit_verify_last_warn_at = 0,
    exit_verify_source = nil,
    human_idle_move_due_at = 0,
    revive_started_at = 0,
    revive_last_warn_at = 0,
    revive_clicked_at = 0,
    revive_click_count = 0,
    revive_resume_ready_at = 0,
    f5_launch_pid = 0,
    f5_launch_hwnd = nil,
    f5_allow_ui_only_exit = false,
    f6_loop_active = false,
    f6_loop_round = 0,
    f6_loop_total_rounds = F6_LOOP_TOTAL_ROUNDS,
    f6_loop_phase = nil,
    f6_loop_started_at = 0,
    f6_loop_deadline_at = 0,
    f6_loop_exit_pending = false,
    f6_loop_wait_until = 0,
    f6_loop_cycle_pid = 0
}

key_latch = {}
exit_latch = false
initialized = false
last_init_error = nil
next_init_retry_at = 0
started_hotkey = false
hotkey_owner_last_write_at = 0
random_seeded = false
resume_snapshot = nil

function trim(text)
    if not text then
        return ""
    end
    return (tostring(text):gsub("^%s+", ""):gsub("%s+$", ""))
end

function read_text(path)
    local file = io.open(path, "rb")
    if not file then
        return nil
    end

    local data = file:read("*a")
    file:close()
    return data
end

function TASK_MODE.label(mode_id)
    local numeric_mode = tonumber(mode_id)
    return TASK_MODE.LABELS[numeric_mode] or ("未知模式(" .. tostring(mode_id) .. ")")
end

function TASK_MODE.build_context()
    return {
        nav = nav,
        sys = sys,
        log = log,
        hotkey = hotkey,
        keybd = keybd,
        mouse = mouse,
        proc = proc,
        wnd = wnd,
        driver = driver,
        process_name = PROCESS_NAME,
        runtime_mode = MODE,
        resolve_project_path = resolve_project_path,
        read_text = read_text,
        trim = trim
    }
end

function avepoint_as_number(value)
    if type(value) == "number" then
        return value
    end
    if type(value) == "string" then
        return tonumber(value)
    end
    return nil
end

function avepoint_find_named_number(tbl, keys, depth, seen)
    if type(tbl) ~= "table" then
        return nil, nil
    end

    local max_depth = tonumber(depth)
    if max_depth ~= nil and max_depth < 0 then
        return nil, nil
    end

    seen = seen or {}
    if seen[tbl] then
        return nil, nil
    end
    seen[tbl] = true

    for _, key in ipairs(keys or {}) do
        local value = avepoint_as_number(tbl[key])
        if value ~= nil then
            return value, tostring(key)
        end
    end

    if max_depth ~= nil and max_depth == 0 then
        return nil, nil
    end

    local next_depth = max_depth ~= nil and (max_depth - 1) or nil
    for key, value in pairs(tbl) do
        if type(value) == "table" then
            local nested_value, nested_source = avepoint_find_named_number(value, keys, next_depth, seen)
            if nested_value ~= nil then
                local prefix = tostring(key)
                if nested_source and nested_source ~= "" then
                    return nested_value, prefix .. "." .. tostring(nested_source)
                end
                return nested_value, prefix
            end
        end
    end

    return nil, nil
end

function avepoint_find_named_number_fuzzy(tbl, patterns, depth, seen)
    if type(tbl) ~= "table" then
        return nil, nil
    end

    local max_depth = tonumber(depth)
    if max_depth ~= nil and max_depth < 0 then
        return nil, nil
    end

    seen = seen or {}
    if seen[tbl] then
        return nil, nil
    end
    seen[tbl] = true

    for key, value in pairs(tbl) do
        local number_value = avepoint_as_number(value)
        if number_value ~= nil then
            local key_text = tostring(key or ""):lower()
            local is_max_field = key_text:find("max", 1, true) ~= nil
                or key_text:find("maximum", 1, true) ~= nil
            if not is_max_field then
                for _, pattern in ipairs(patterns or {}) do
                    local needle = tostring(pattern or ""):lower()
                    if needle ~= "" and key_text:find(needle, 1, true) then
                        return number_value, tostring(key)
                    end
                end
            end
        elseif type(value) == "table" and (max_depth == nil or max_depth > 0) then
            local nested_value, nested_source = avepoint_find_named_number_fuzzy(
                value,
                patterns,
                max_depth ~= nil and (max_depth - 1) or nil,
                seen
            )
            if nested_value ~= nil then
                local prefix = tostring(key)
                if nested_source and nested_source ~= "" then
                    return nested_value, prefix .. "." .. tostring(nested_source)
                end
                return nested_value, prefix
            end
        end
    end

    return nil, nil
end

function avepoint_extract_player_hp(info)
    local value, source = avepoint_find_named_number(info, {
        "hp", "HP", "Hp",
        "curHp", "CurHp", "curHP", "CurHP",
        "currentHp", "CurrentHp", "currentHP", "CurrentHP",
        "health", "Health",
        "curHealth", "CurHealth",
        "currentHealth", "CurrentHealth",
        "life", "Life",
        "curLife", "CurLife",
        "currentLife", "CurrentLife",
        "blood", "Blood",
        "curBlood", "CurBlood",
        "currentBlood", "CurrentBlood",
        "血量", "当前血量"
    }, 4)
    if value ~= nil then
        return value, source
    end

    return avepoint_find_named_number_fuzzy(info, {
        "hp", "health", "life", "blood", "血量"
    }, 4)
end

function avepoint_launch_torchlight_game()
    local existing_pid = nil
    if type(proc) == "table" and type(proc.list) == "function" then
        local list = proc.list()
        local wanted = tostring(PROCESS_NAME or ""):lower()
        if type(list) == "table" then
            for _, item in ipairs(list) do
                if tostring(item and item.name or ""):lower() == wanted then
                    existing_pid = tonumber(item.pid) or 0
                    break
                end
            end
        end
    end

    if existing_pid == nil
        and type(proc) == "table"
        and type(proc.exists) == "function"
        and proc.exists(PROCESS_NAME)
    then
        existing_pid = 0
    end

    if existing_pid ~= nil then
        if not initialized then
            initialized = false
            last_init_error = nil
            next_init_retry_at = 0
            if existing_pid > 0 then
                log.info(string.format(
                    "Game already running; init retry requested | pid=%d name=%s",
                    existing_pid,
                    PROCESS_NAME
                ))
            else
                log.info("Game already running; init retry requested | name=" .. tostring(PROCESS_NAME))
            end
        else
            if existing_pid > 0 then
                log.info(string.format(
                    "Game already running | pid=%d name=%s",
                    existing_pid,
                    PROCESS_NAME
                ))
            else
                log.info("Game already running | name=" .. tostring(PROCESS_NAME))
            end
        end
        return true
    end

    local cmd = 'cmd.exe /c start "" "taptap://taptap.com/app?app_id=172664&auto_launch=true&ch_src=desktop---&game_type=pc&platform=pc"'
    local launcher_pid = proc.create(cmd, nil, false)
    if not launcher_pid or launcher_pid <= 0 then
        return false, "proc.create failed for auto launch command."
    end

    initialized = false
    last_init_error = nil
    next_init_retry_at = 0
    log.info(string.format(
        "Game auto launch triggered | launcher_pid=%d process=%s",
        launcher_pid,
        PROCESS_NAME
    ))
    return true
end

function hotkey_owner_lock_path()
    return resolve_project_path(HOTKEY_OWNER_LOCK_FILE)
end

function write_hotkey_owner_lock()
    local now = sys.time()
    local file = io.open(hotkey_owner_lock_path(), "wb")
    if not file then
        return false
    end

    file:write(tostring(now))
    file:close()
    hotkey_owner_last_write_at = now
    return true
end

function remove_hotkey_owner_lock()
    os.remove(hotkey_owner_lock_path())
    hotkey_owner_last_write_at = 0
end

function extract_json_string(text, key)
    if not text or text == "" then
        return nil
    end

    local pattern = '"' .. key:gsub("([^%w])", "%%%1") .. '"%s*:%s*"(.-)"'
    local value = text:match(pattern)
    if not value then
        return nil
    end

    value = value:gsub('\\"', '"'):gsub('\\\\', '\\')
    value = trim(value)
    if value == "" then
        return nil
    end

    return value
end

function TASK_MODE.extract_json_number(text, key)
    if not text or text == "" then
        return nil
    end

    local pattern = '"' .. key:gsub("([^%w])", "%%%1") .. '"%s*:%s*(-?%d+)'
    local value = text:match(pattern)
    if not value then
        return nil
    end

    return tonumber(value)
end

function TASK_MODE.read_configured()
    local config_path = resolve_project_path(guard.engine_config)
    local config_text = read_text(config_path)
    local configured_mode = TASK_MODE.extract_json_number(config_text, TASK_MODE.CONFIG_KEY)

    if configured_mode == TASK_MODE.GOLD
        or configured_mode == TASK_MODE.LEVELING
        or configured_mode == TASK_MODE.OPEN_MAP
    then
        return configured_mode, TASK_MODE.label(configured_mode)
    end

    if configured_mode ~= nil then
        log.warn(string.format(
            "Invalid %s=%s in %s, fallback to %d(%s)",
            TASK_MODE.CONFIG_KEY,
            tostring(configured_mode),
            tostring(guard.engine_config),
            TASK_MODE.DEFAULT,
            TASK_MODE.label(TASK_MODE.DEFAULT)
        ))
    end

    return TASK_MODE.DEFAULT, TASK_MODE.label(TASK_MODE.DEFAULT)
end

function TASK_MODE.load_runner(mode_id)
    if mode_id == TASK_MODE.LEVELING then
        local chunk = loadfile_with_bytecode_fallback("scripts/AvePointLeveling.lua", "AvePointLeveling")
        local ok, runner = pcall(chunk)
        if not ok then
            return nil, runner
        end
        if type(runner) ~= "table" then
            return nil, "AvePointLeveling.lua must return a table"
        end
        return runner
    end

    if mode_id == TASK_MODE.OPEN_MAP then
        return nil, "开图模式尚未实现，请先把 avepointTaskMode 改成 1 或 2"
    end

    return nil, "Unsupported task mode: " .. tostring(mode_id)
end

function TASK_MODE.refresh_config()
    TASK_MODE.configured_id, TASK_MODE.configured_name = TASK_MODE.read_configured()
    return TASK_MODE.configured_id, TASK_MODE.configured_name
end

pcall(function()
    local global_runtime_mode = nil
    if type(_G) == "table" then
        global_runtime_mode = trim(_G.__CUNNEI_AVEPOINT_RUNTIME_MODE or ""):lower()
    end

    local config_text = read_text(resolve_project_path(guard.engine_config))
    local runtime_mode = global_runtime_mode
    if runtime_mode ~= "api" and runtime_mode ~= "driver" then
        runtime_mode = trim(extract_json_string(config_text, "avepointRuntimeMode") or ""):lower()
    end
    if runtime_mode == "api" or runtime_mode == "driver" then
        MODE = runtime_mode
    end

    local bool_value = nil
    if type(_G) == "table" and type(_G.__CUNNEI_AVEPOINT_PROTECT_PROCESS) == "boolean" then
        bool_value = _G.__CUNNEI_AVEPOINT_PROTECT_PROCESS and "true" or "false"
    else
        local bool_pattern = '"avepointProtectProcess"%s*:%s*(true|false)'
        bool_value = config_text and config_text:match(bool_pattern) or nil
    end
    if bool_value == "true" then
        guard.protect_process = true
    elseif bool_value == "false" then
        guard.protect_process = false
    end
end)
