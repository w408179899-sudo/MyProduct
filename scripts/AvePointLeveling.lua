local M = {}

function M.loadfile_with_bytecode_fallback_local(path, label)
    local candidates = { path }
    if type(path) == "string" and path ~= "" then
        if path:sub(-4):lower() == ".lua" then
            candidates[#candidates + 1] = path:sub(1, -5) .. ".luac"
        elseif path:sub(-5):lower() ~= ".luac" then
            candidates[#candidates + 1] = path .. ".luac"
        end
    end

    local last_err = nil
    local errors = {}
    for _, candidate in ipairs(candidates) do
        local chunk, err = loadfile(candidate)
        if chunk then
            return chunk
        end
        last_err = err
        errors[#errors + 1] = string.format("%s: %s", tostring(candidate), tostring(err))
    end

    local detail = #errors > 0 and table.concat(errors, "\n") or tostring(last_err)
    error(string.format("load %s failed:\n%s", tostring(label or path), detail))
end

function M.load_leveling_support_module(require_name, require_name_alt, file_path, label)
    local ok, mod = pcall(require, require_name)
    if ok and type(mod) == "table" then
        return mod
    end

    ok, mod = pcall(require, require_name_alt)
    if ok and type(mod) == "table" then
        return mod
    end

    local chunk = M.loadfile_with_bytecode_fallback_local(file_path, label)
    return chunk()
end

M._leveling_config = M.load_leveling_support_module(
    "AvePointLevelingConfig",
    "scripts.AvePointLevelingConfig",
    "scripts/AvePointLevelingConfig.lua",
    "AvePointLevelingConfig"
)

M._leveling_policy = M.load_leveling_support_module(
    "AvePointLevelingPolicy",
    "scripts.AvePointLevelingPolicy",
    "scripts/AvePointLevelingPolicy.lua",
    "AvePointLevelingPolicy"
)

M._leveling_treasure = M.load_leveling_support_module(
    "AvePointLevelingTreasure",
    "scripts.AvePointLevelingTreasure",
    "scripts/AvePointLevelingTreasure.lua",
    "AvePointLevelingTreasure"
)

local UPDATE_INTERVAL_MS = 100
local HEARTBEAT_INTERVAL_MS = 3000
local ACTION_INTERVAL_MS = 550
local MOVE_INTERVAL_MS = 900
local TASK_REFRESH_INTERVAL_MS = 1200
local TASK_PATH_FETCH_POLL_INTERVAL_MS = 300
local TASK_STALE_KEEP_MS = 5000
local INPUT_PREPARE_INTERVAL_MS = 2000
local INPUT_PREPARE_SETTLE_MS = 60
local NAV_RETRY_INTERVAL_MS = 1500
local TASK_BUTTON_RETRY_INTERVAL_MS = 1500
local TASK_BUTTON_SETTLE_MS = 500
local TASK_BUTTON_PATH_FETCH_TIMEOUT_MS = 4500
local TASK_BUTTON_KEEPALIVE_INTERVAL_MS = 3500
local TASK_BUTTON_SOFT_REFRESH_INTERVAL_MS = 12000
local TASK_BUTTON_SOFT_REFRESH_MIN_DISTANCE = 2600
local TASK_BUTTON_SOFT_REFRESH_MOVE_GAP_MS = 600
local PLAYER_INFO_REFRESH_INTERVAL_MS = 400
local MAP_INFO_REFRESH_INTERVAL_MS = 1000
local NPC_SCAN_INTERVAL_MS = 500
local MONSTER_SCAN_INTERVAL_MS = 250
local NPC_INTERACT_DISTANCE = 320
local NPC_TASK_TARGET_MAX_DISTANCE = 220
local TASK_MONSTER_PLAYER_DISTANCE = 760
local TASK_MONSTER_TARGET_DISTANCE = 520
local TARGET_REACHED_DISTANCE = 140
local TASK_INTERACTION_APPROACH_DISTANCE = 70
local INTERACTION_PROMPT_SCAN_INTERVAL_MS = 300
local EXIT_PORTAL_SCAN_INTERVAL_MS = 300
local TASK_COMBAT_CLEAR_SETTLE_MS = 1800
local TASK_COMBAT_HARD_TRIGGER_MS = 4500
local TASK_COMBAT_HARD_MONSTER_COUNT = 6
local TASK_COMBAT_HARD_NEAREST_DISTANCE = 180
local TASK_COMBAT_HARD_LOW_HP_RATIO = 0.78
local TASK_COMBAT_KITE_RADIUS = 2880
local TASK_COMBAT_KITE_SWITCH_MS = 700
local TASK_COMBAT_KITE_POINT_ARRIVE_DISTANCE = 220
local TASK_COMBAT_KITE_ANCHOR_REBUILD_DISTANCE = 80
local TASK_PATH_POINT_ARRIVE_TOLERANCE = math.max(80, tonumber(ARRIVE_TOLERANCE) or 120)
local TASK_PATH_REPATH_INTERVAL_MS = math.max(500, tonumber(REPATH_INTERVAL_MS) or 1500)
local TASK_PATH_PROGRESS_RESET_DISTANCE = math.max(40, tonumber(MAP_ROUTE_STUCK_PROGRESS_RESET_DISTANCE) or 80)
local TASK_PATH_STUCK_SKIP_MS = math.max(3000, tonumber(MAP_ROUTE_STUCK_SKIP_MS) or 10000)
local COMBAT_PULSE_NEAR_TARGET_DISTANCE = 220
local PATH_LOOKAHEAD_POINTS = 6
local PATH_MIN_ADVANCE_DISTANCE = 1200
local TASK_PATH_COMPRESS_MIN_DISTANCE = 1150
local TASK_PATH_COMPRESS_TURN_MIN_DISTANCE = 360
local TASK_PATH_COMPRESS_TURN_COS_THRESHOLD = 0.82
local PROGRESS_RESET_DISTANCE = 80
local STUCK_RETRY_INTERVAL_MS = 3000
local STUCK_MOVE_GRACE_MS = 1600
local TASK_PATH_TARGET_STICK_MS = math.max(2200, math.floor(TASK_PATH_REPATH_INTERVAL_MS * 1.4))
local TASK_PATH_TARGET_FORCE_INDEX_DELTA = 5
local TASK_PATH_REANCHOR_DISTANCE = 900
local TASK_PATH_REANCHOR_ADVANTAGE_DISTANCE = 180
local TASK_PATH_REANCHOR_INDEX_DELTA = 3
local TASK_PATH_LOST_REFRESH_AFTER_MS = 1800
local TASK_PATH_DEVIATION_REFRESH_DISTANCE = 360
local TASK_PATH_DEVIATION_REFRESH_COOLDOWN_MS = 2500
local MOVE_COMBAT_GUARD_MS = 650
local NAV_WORKER_CHECK_INTERVAL_MS = 300
local NAV_WORKER_RESTART_INTERVAL_MS = 1200
local NAV_WORKER_HEARTBEAT_STALE_MS = 3000
M.NAV_WORKER_ROUTE_POINT_SHARE_LIMIT = 1024
local LEVELING_USE_NAV_WORKER = M._leveling_config
    and M._leveling_config.LEVELING_USE_NAV_WORKER == true
M.TASK_PATH_WORKER_ROUTE_MODE = not (
    M._leveling_config and M._leveling_config.TASK_PATH_WORKER_ROUTE_MODE == false
)
M.LOADING_TRANSITION_REACQUIRE_CFG = {
    settle_ms = 900,
    origin_distance = 1200,
    path_distance = TASK_PATH_REANCHOR_DISTANCE * 2,
    target_distance = 2200
}
M.TASK_PATH_WORKER_MAX_POINTS = math.min(
    M.NAV_WORKER_ROUTE_POINT_SHARE_LIMIT,
    math.max(16, tonumber(M._leveling_config and M._leveling_config.TASK_PATH_WORKER_MAX_POINTS) or 128)
)
M.TASK_PATH_USE_RAW_PATH = M._leveling_config
    and M._leveling_config.TASK_PATH_USE_RAW_PATH == true
local ACTION_KEY_HOLD_MS = 120
local ACTION_MOUSE_HOLD_MS = 34
local DIALOGUE_COOLDOWN_MS = 2500
local DIALOGUE_ESCAPE_DELAY_MS = 900
local DIALOGUE_ESCAPE_RETRY_MS = 1000
local DIALOGUE_CONFIRM_TIMEOUT_MS = 2500
local DIALOGUE_UI_PROBE_INTERVAL_MS = 250
local POST_DIALOGUE_SETTLE_MS = 1200
local TASK_UPDATE_SETTLE_MS = 2600
local POST_UI_PAUSE_MS = 900
local FOLLOW_MOVE_PULSE_INTERVAL_MS = 550
local FOLLOW_MOVE_PULSE_MIN_DISTANCE = 260
local POTION_THRESHOLD_RATIO = 0.60
local POTION_COOLDOWN_MS = 2500
local LOG_THROTTLE_MS = 3000
local MONSTER_SCAN_LOG_INTERVAL_MS = 2000
local REVIVE_CLICK_RETRY_INTERVAL_MS = 3000
local REVIVE_READY_STABLE_MS = 1000
local NAV_WORKER_SHARE_PREFIX_BASE = "avepoint_leveling_nav"
M.TASK_FOLLOW_MOVE_INTERVAL_MS = math.max(120, tonumber(M._leveling_config and M._leveling_config.TASK_FOLLOW_MOVE_INTERVAL_MS) or 220)
M.TASK_POS_MOVE_INTERVAL_MS = math.max(120, tonumber(M._leveling_config and M._leveling_config.TASK_POS_MOVE_INTERVAL_MS) or 250)
M.TASK_COMBAT_KITE_ASYNC_ROUTE_WORKER = not (
    M._leveling_config and M._leveling_config.TASK_COMBAT_KITE_ASYNC_ROUTE_WORKER == false
)
M.VK_A = 0x41
local VK_Q = 0x51
local schedule_task_refresh_after_transition
local VK_W = 0x57
local VK_D = 0x44
local VK_ESCAPE = 0x1B

local DIALOGUE_UI_PATTERNS = {
    "dialog",
    "dialogue",
    "talk",
    "conversation",
    "story",
    "subtitle",
    "skipbutton",
    "skip",
    "continue",
    "next",
    "leave",
    "accept",
    "confirm"
}

local DIALOGUE_ESCAPE_SAFE_PATTERNS = {
    "skipbutton",
    "skip",
    "jumpbtn",
    "cinematic",
    "movie"
}

local DIALOGUE_UI_EXCLUDE_PATTERNS = {
    "fastentranceview",
    "fightinteractiveview",
    "fightfailure",
    "closebtn2",
    "rebornbtn",
    "taskitem_c.widgettree.taskbtn",
    "dialogueitem_c.widgettree.paddingbg",
    "dialoguetalk_c.widgettree.paddingbg"
}

local NPC_DIALOGUE_TRIGGER_DISTANCE = math.min(NPC_INTERACT_DISTANCE, 150)
local NPC_DIALOGUE_MONSTER_BLOCK_DISTANCE = 220

local MAIN_TASK_BUTTON_STEP = {
    label = "Main task TaskBtn",
    distance_anchor_exact_text = "",
    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn",
    distance_min = 18.0,
    distance_max = 52.0,
    include_patterns = {
        "UIButton Transient.GameEngine.CoreGameInstance.TaskItem_C.WidgetTree.TaskBtn"
    },
    hint_client_x = 89.907120,
    hint_client_y = 235.181610,
    hint_ratio_x = 0.062435,
    hint_ratio_y = 0.261313,
    hint_max_distance = 80.000
}

local REVIVE_AT_CHECKPOINT_STEP = {
    label = "\u{8BB0}\u{5F55}\u{70B9}\u{590D}\u{6D3B}\u{6309}\u{94AE}",
    distance_anchor_exact_text = "\u{8BB0}\u{5F55}\u{70B9}\u{590D}\u{6D3B}",
    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightFailure_C.WidgetTree.InsideRebornBtn.WidgetTree.ClickBtn",
    distance_min = 8.661942,
    distance_max = 9.661942,
    include_patterns = {
        "UIButton Transient.GameEngine.CoreGameInstance.FightFailure_C.WidgetTree.InsideRebornBtn.WidgetTree.ClickBtn"
    },
    hint_client_x = 569.913635,
    hint_client_y = 852.957703,
    hint_ratio_x = 0.395773,
    hint_ratio_y = 0.947731,
    hint_max_distance = 80.000
}

local REVIVE_REENTER_STEP = {
    label = "閲嶆柊鎸戞垬鎸夐挳",
    distance_anchor_exact_text = "閲嶆柊鎸戞垬",
    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightFailure_C.WidgetTree.ReenterRebornBtn.WidgetTree.ClickBtn",
    distance_min = 8.661942,
    distance_max = 9.661942,
    include_patterns = {
        "UIButton Transient.GameEngine.CoreGameInstance.FightFailure_C.WidgetTree.ReenterRebornBtn.WidgetTree.ClickBtn"
    },
    hint_client_x = 574.406433,
    hint_client_y = 852.957703,
    hint_ratio_x = 0.398893,
    hint_ratio_y = 0.947731,
    hint_max_distance = 80.000
}

local INTERACTION_PROMPT_STEP = {
    label = "\u{4EA4}\u{4E92}\u{4E2D}\u{6309}\u{94AE}",
    distance_anchor_exact_text = "\u{4EA4}\u{4E92}\u{4E2D}",
    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.FunctionBtn",
    distance_min = 165.231248,
    distance_max = 170.231248,
    include_patterns = {
        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.FunctionBtn"
    },
    hint_client_x = 700.204834,
    hint_client_y = 742.439941,
    hint_ratio_x = 0.486253,
    hint_ratio_y = 0.824933,
    hint_max_distance = 80.000
}

local EXIT_PORTAL_STEP = {
    label = "\u{963F}\u{745E}\u{5A05}\u{6309}\u{94AE}",
    distance_anchor_exact_text = "\u{963F}\u{745E}\u{5A05}",
    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
    distance_min = 168.199816,
    distance_max = 173.199816,
    include_patterns = {
        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
    },
    hint_client_x = 705.204834,
    hint_client_y = 726.439941,
    hint_ratio_x = 0.489726,
    hint_ratio_y = 0.807155,
    hint_max_distance = 180.000,
    prefer_hint_fallback = true
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
        settle_ms = POST_DIALOGUE_SETTLE_MS
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
        settle_ms = POST_DIALOGUE_SETTLE_MS
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
        settle_ms = TASK_BUTTON_SETTLE_MS
    }
}

local MAP_TASK_CONFIGS = {
    ["\u{8FDC}\u{53E4}\u{901A}\u{9053}"] = {
        transitions = {
            {
                key = "wanderer_boots_portal",
                label = "\u{8FDC}\u{53E4}\u{901A}\u{9053}\u{5F}\u{6E38}\u{8361}\u{8005}\u{957F}\u{9774}\u{4F20}\u{9001}",
                trigger = {
                    x = 2568.74,
                    y = 19312.33,
                    radius = 520
                },
                settle_ms = POST_DIALOGUE_SETTLE_MS,
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
    ["\u{4E0A}\u{53E4}\u{6218}\u{573A}"] = {
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
            settle_ms = POST_DIALOGUE_SETTLE_MS
        }
    }
}

M.OBJECTIVE_POINT_CONFIGS = {
    {
        key = "controlled_goblin_boss_room",
        mode = "boss_kite",
        x = 12351.00,
        y = -7100.00,
        radius = 520,
        trigger_distance = 520,
        skip_direct_interact = true,
        allow_any_monster = true,
        force_kite = true,
        task_patterns = {
            "\u{51FB}\u{8D25}\u{88AB}\u{64CD}\u{7EB5}\u{7684}\u{54E5}\u{5E03}\u{6797}",
            "\u{88AB}\u{64CD}\u{7EB5}\u{7684}\u{54E5}\u{5E03}\u{6797}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    },
    {
        key = "dragonbone_griffin_boss_room",
        mode = "boss_kite",
        x = 2852.00,
        y = 4494.00,
        radius = 1200,
        kite_radius = 3600,
        trigger_distance = 900,
        skip_direct_interact = true,
        allow_any_monster = true,
        force_kite = true,
        task_patterns = {
            "\u{6DF1}\u{5165}\u{9F99}\u{9AA8}\u{5C71}\u{810A}\u{8179}\u{5730}",
            "\u{51FB}\u{8D25}\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{76D8}\u{8E1E}\u{5728}\u{6B64}\u{7684}\u{72EE}\u{9E6B}",
            "\u{9AD8}\u{539F}\u{9F99}\u{9AA8}\u{72EE}\u{9E6B}"
        },
        exclude_task_patterns = {
            "\u{4EA4}\u{8C08}",
            "\u{5BF9}\u{8BDD}"
        }
    }
}

M.ENABLE_MAP_RUNTIME_DETECTION = false

M.FORCE_KITE_MONSTER_NAMES = {
    ["\u{88AB}\u{64CD}\u{7EB5}\u{7684}\u{54E5}\u{5E03}\u{6797}\u{5F13}\u{7BAD}\u{624B}"] = true
}

M.GUIDE_SKIP_STEP = {
    label = "\u{52A0}\u{6587}\u{6309}\u{94AE}",
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
    label = "\u{7834}\u{788E}\u{94C1}\u{65A7}\u{6309}\u{94AE}",
    distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn",
    include_patterns = {
        "UIButton Transient.GameEngine.CoreGameInstance.FightInteractiveView_C.WidgetTree.PortalBtn"
    },
    hint_client_x = 697.204834,
    hint_client_y = 724.439941,
    hint_ratio_x = 0.484170,
    hint_ratio_y = 0.804933,
    hint_max_distance = 80.000
}

M.ROUTE_POINT_ACTIONS = {}
M.TREASURE_DUNGEON_CONFIGS = {}

if type(M._leveling_config) == "table" then
    if type(M._leveling_config.TASK_OBJECTIVE_BUTTON_STEPS) == "table" then
        M.TASK_OBJECTIVE_BUTTON_STEPS = M._leveling_config.TASK_OBJECTIVE_BUTTON_STEPS
    end
    if type(M._leveling_config.MAP_TASK_CONFIGS) == "table" then
        MAP_TASK_CONFIGS = M._leveling_config.MAP_TASK_CONFIGS
    end
    if type(M._leveling_config.ENABLE_MAP_RUNTIME_DETECTION) == "boolean" then
        M.ENABLE_MAP_RUNTIME_DETECTION = M._leveling_config.ENABLE_MAP_RUNTIME_DETECTION
    end
    if type(M._leveling_config.OBJECTIVE_POINT_CONFIGS) == "table" then
        M.OBJECTIVE_POINT_CONFIGS = M._leveling_config.OBJECTIVE_POINT_CONFIGS
    end
    if type(M._leveling_config.TASK_NAME_CONFIGS) == "table" then
        M.TASK_NAME_CONFIGS = M._leveling_config.TASK_NAME_CONFIGS
    end
    if type(M._leveling_config.FORCE_KITE_MONSTER_NAMES) == "table" then
        M.FORCE_KITE_MONSTER_NAMES = M._leveling_config.FORCE_KITE_MONSTER_NAMES
    end
    if type(M._leveling_config.GUIDE_SKIP_STEP) == "table" then
        M.GUIDE_SKIP_STEP = M._leveling_config.GUIDE_SKIP_STEP
    end
    if type(M._leveling_config.GLOBAL_TASK_PORTAL_STEP) == "table" then
        M.GLOBAL_TASK_PORTAL_STEP = M._leveling_config.GLOBAL_TASK_PORTAL_STEP
    end
    if type(M._leveling_config.ROUTE_POINT_ACTIONS) == "table" then
        M.ROUTE_POINT_ACTIONS = M._leveling_config.ROUTE_POINT_ACTIONS
    end
    if type(M._leveling_config.TREASURE_DUNGEON_CONFIGS) == "table" then
        M.TREASURE_DUNGEON_CONFIGS = M._leveling_config.TREASURE_DUNGEON_CONFIGS
    end
end

local state = {}

function M.should_suspend_treasure_task_refresh()
    return type(M._leveling_treasure) == "table"
        and type(M._leveling_treasure.should_suspend_task_refresh) == "function"
        and M._leveling_treasure.should_suspend_task_refresh(state) == true
end

function M.publish_current_task_name()
    _G.AVEPOINT_CURRENT_TASK_NAME = state.current_task_name
    _G.AVEPOINT_CURRENT_TASK_DETAIL = state.current_task_detail
    if type(state.current_task_detail) == "string" and trim(state.current_task_detail) ~= "" then
        _G.AVEPOINT_LAST_TASK_DETAIL = state.current_task_detail
    end
    if type(state.current_task_name) == "string" and state.current_task_name ~= "" then
        local normalized = trim(tostring(state.current_task_name or ""))
        local generic_mainline = normalized:match("^主线%s+") ~= nil
        local side_task = normalized:match("^支线%s+") ~= nil
            or normalized:find("藏宝地", 1, true) ~= nil
        local looks_like_known_map = type(M.is_known_map_name) == "function" and M.is_known_map_name(normalized) or false
        if generic_mainline then
            local stripped = trim(normalized:gsub("^主线%s*", ""))
            if stripped ~= "" and not M.is_known_map_name(stripped) then
                _G.AVEPOINT_LAST_TASK_NAME = stripped
            end
            return
        end
        if side_task or looks_like_known_map then
            return
        end
        _G.AVEPOINT_LAST_TASK_NAME = state.current_task_name
    end
end

function M.current_task_log_name()
    local task_name = tostring(state.current_task_name or "")
    if task_name == "" or task_name:match("^主线%s+") then
        local fallback = tostring(_G.AVEPOINT_LAST_TASK_NAME or "")
        if fallback ~= ""
            and fallback:match("^支线%s+") == nil
            and fallback:find("藏宝地", 1, true) == nil
        then
            task_name = fallback
        end
    end
    return task_name
end

function M.current_task_log_detail()
    local task_detail = trim(tostring(state.current_task_detail or ""))
    if task_detail == "" then
        task_detail = trim(tostring(_G.AVEPOINT_LAST_TASK_DETAIL or ""))
    end
    return task_detail
end
local log_heartbeat
local clear_task_combat_state
local clear_runtime_objective_caches
local try_click_exit_portal

function M.clear_route_point_action_board_state()
    state.route_point_action_active_key = nil
    state.route_point_action_active_state_key = nil
    state.route_point_action_board_entered_at = 0
    state.route_point_action_board_next_interact_at = 0
    state.route_point_action_board_deadline_at = 0
end

function M.clear_route_point_action_npc_dialogue_state()
    state.route_point_action_dialogue_active_key = nil
    state.route_point_action_dialogue_active_state_key = nil
    state.route_point_action_dialogue_entered_at = 0
    state.route_point_action_dialogue_next_interact_at = 0
    state.route_point_action_dialogue_deadline_at = 0
end

function M.clear_route_point_action_objective_state()
    state.route_point_action_objective_active_key = nil
    state.route_point_action_objective_active_state_key = nil
    state.route_point_action_objective_entered_at = 0
    state.route_point_action_objective_next_probe_at = 0
    state.route_point_action_objective_deadline_at = 0
end

function M.clear_route_point_action_route_state()
    state.route_point_action_route_active_key = nil
    state.route_point_action_route_active_state_key = nil
    state.route_point_action_route_started_at = 0
    state.route_point_action_route_deadline_at = 0
    state.route_point_action_route_index = 0
    state.route_point_action_route_next_retry_at = 0
end

function M.clear_route_point_action_route_wait_state()
    state.route_point_action_route_wait_reacquire_until = 0
    state.route_point_action_route_wait_key = nil
    state.route_point_action_route_wait_task_name = nil
    state.route_point_action_route_wait_task_detail = nil
end

function M.clear_task_dialogue_flow_state()
    state.task_dialogue_flow_key = nil
    state.task_dialogue_flow_step_index = 0
    state.task_dialogue_flow_started_at = 0
    state.task_dialogue_flow_deadline_at = 0
    state.task_dialogue_flow_next_retry_at = 0
    state.task_dialogue_flow_last_origin = nil
end

function M.clear_post_dialogue_flow_state()
    state.post_dialogue_flow_key = nil
    state.post_dialogue_flow_steps = nil
    state.post_dialogue_flow_step_index = 0
    state.post_dialogue_flow_started_at = 0
    state.post_dialogue_flow_deadline_at = 0
    state.post_dialogue_flow_next_retry_at = 0
    state.post_dialogue_flow_task_name = nil
    state.post_dialogue_flow_origin = nil
    state.post_dialogue_flow_skip_dialogue_jump = false
end

function M.clear_npc_dialogue_combat_retry_state()
    state.npc_dialogue_combat_retry_active = false
    state.npc_dialogue_combat_retry_source = nil
    state.npc_dialogue_combat_retry_task_name = nil
    state.npc_dialogue_combat_retry_task_detail = nil
    state.npc_dialogue_combat_retry_npc_label = nil
    state.npc_dialogue_combat_retry_route_action_key = nil
    state.npc_dialogue_combat_retry_point_x = nil
    state.npc_dialogue_combat_retry_point_y = nil
    state.npc_dialogue_combat_retry_point_z = nil
    state.npc_dialogue_combat_retry_search_radius = nil
    state.npc_dialogue_combat_retry_interact_radius = nil
    state.npc_dialogue_combat_retry_move_interval_ms = nil
    state.npc_dialogue_combat_retry_deadline_at = 0
    state.npc_dialogue_combat_retry_next_retry_at = 0
    state.npc_dialogue_combat_retry_combat_seen = false
end

function M.clear_post_combat_loot_state()
    state.post_combat_loot_active_key = nil
    state.post_combat_loot_started_at = 0
    state.post_combat_loot_next_press_at = 0
    state.post_combat_loot_last_item_at = 0
    state.post_combat_loot_duration_ms = nil
    state.post_combat_loot_max_duration_ms = nil
    state.post_combat_loot_press_interval_ms = nil
    state.post_combat_loot_empty_settle_ms = nil
end

function M.is_task_combat_or_post_loot_active()
    local stage_name = tostring(state.stage or "")
    return state.task_combat_force_kite == true
        or stage_name == "task_combat"
        or stage_name == "task_combat_kite"
        or stage_name == "task_combat_settle"
        or stage_name == "task_combat_complete_settle"
        or stage_name == "post_combat_loot"
end

local function clear_task_target_state()
    state.last_task_signature = nil
    state.task_path = nil
    state.task_path_route = nil
    state.task_path_raw_count = 0
    state.task_path_compress_mode = nil
    state.task_pos = nil
    state.task_target = nil
    state.task_target_updated_at = 0
    state.task_path_count = 0
    state.task_path_refresh_requested = true
    state.task_path_wait_until = 0
    state.require_task_button_refresh_reason = nil
    state.task_pos_reject_until = 0
    state.task_pos_reject_reason = nil
    -- force_task_path_reacquire_* must survive ordinary target clears until a
    -- fresh task_path is adopted or the guarded retry window expires.
    state.next_task_path_deviation_refresh_at = 0
    state.next_follow_idle_refresh_at = 0
    state.last_task_path_sync_at = 0
    state.nav_worker_path_route_signature = nil
    -- Keep route_version monotonic across ordinary main-task reacquires so the
    -- nav worker can distinguish a new task_path from a previous path_route.
    state.nav_worker_path_route_version = tonumber(state.nav_worker_path_route_version) or 0
    state.nav_worker_path_route_window_start = 0
    state.nav_worker_path_route_window_end = 0
    state.nav_worker_path_route_direction = 1
    state.nav_worker_path_route_path_signature = nil
    state.last_move_attempt_at = 0
    state.last_move_failure_at = 0
    state.move_failure_streak = 0
    state.task_combat_started_at = 0
    state.task_combat_last_seen_at = 0
    state.task_combat_last_count = 0
    state.task_combat_anchor_x = nil
    state.task_combat_anchor_y = nil
    state.task_combat_anchor_z = nil
    state.task_combat_kite_phase = 0
    state.task_combat_next_kite_switch_at = 0
    state.task_combat_kite_points = nil
    state.task_combat_kite_index = 0
    state.task_combat_kite_template_points = nil
    state.task_combat_kite_switch_ms = nil
    state.task_combat_kite_seamless = false
    state.task_combat_kite_async_worker = false
    state.task_combat_kite_route_worker_signature = nil
    state.task_combat_kite_route_worker_version = 0
    state.task_combat_kite_route_worker_active = false
    state.task_combat_kite_arrive_distance = nil
    state.task_combat_kite_move_interval_ms = nil
    state.task_combat_kite_force_move = false
    state.task_combat_kite_anchor_route_x = nil
    state.task_combat_kite_anchor_route_y = nil
    state.task_combat_kite_anchor_route_z = nil
    state.task_combat_kite_radius = nil
    state.task_combat_force_kite = false
    M.clear_post_combat_loot_state()
    state.terminal_task_locked_name = nil
    state.terminal_task_locked_detail = nil
    state.terminal_task_locked_objective_key = nil
    state.task_reached_unresolved_since = 0
    state.cached_task_objective_button = nil
    state.cached_task_objective_button_error = nil
    state.cached_task_objective_button_key = nil
    state.next_task_objective_button_scan_at = 0
    state.next_task_objective_button_click_at = 0
    state.next_guide_skip_scan_at = 0
    state.next_guide_skip_click_at = 0
    -- Global PortalBtn transition guard must survive task-target clears.
    -- It is reset by reset_state(), not by ordinary main-task reacquire.
    state.route_point_action_board_guard_until = 0
    state.route_point_action_board_guard_key = nil
    M.clear_route_point_action_board_state()
    M.clear_route_point_action_npc_dialogue_state()
    M.clear_route_point_action_objective_state()
    M.clear_route_point_action_route_state()
    M.clear_route_point_action_route_wait_state()
    M.clear_task_dialogue_flow_state()
    M.clear_npc_dialogue_combat_retry_state()
end

function M.clear_task_entry_action_state()
    state.task_entry_action_button_click_at = 0
    state.task_entry_action_center_clicked_at = 0
    state.task_entry_action_next_center_click_at = 0
    state.task_entry_action_map_open_wait_ms = 0
    state.task_entry_action_pre_clicked_at = 0
    state.task_entry_action_send_clicked_at = 0
    state.task_entry_action_locked_cfg = nil
    state.task_entry_action_locked_task_name = nil
    state.task_entry_action_locked_key = nil
end

local function reset_state()
    state.running = false
    state.stage = "idle"
    state.next_tick_at = 0
    state.next_move_at = 0
    state.next_action_at = 0
    state.next_task_refresh_at = 0
    state.next_input_prepare_at = 0
    state.next_nav_retry_at = 0
    state.next_task_button_click_at = 0
    state.task_path_wait_until = 0
    state.next_follow_task_button_refresh_at = 0
    state.next_task_button_soft_refresh_at = 0
    state.next_task_path_deviation_refresh_at = 0
    state.next_follow_idle_refresh_at = 0
    state.next_follow_move_pulse_at = 0
    state.next_npc_scan_at = 0
    state.next_monster_scan_at = 0
    state.next_player_info_refresh_at = 0
    state.last_heartbeat_at = 0
    state.last_exec_trace_at = 0
    state.last_exec_trace_key = nil
    state.last_progress_at = 0
    state.last_progress_x = nil
    state.last_progress_y = nil
    state.last_position_change_at = 0
    state.last_position_change_x = nil
    state.last_position_change_y = nil
    state.last_nav_error = nil
    state.last_task_button_click_at = 0
    state.last_task_path_sync_at = 0
    state.last_move_call_at = 0
    state.last_move_attempt_at = 0
    state.last_move_failure_at = 0
    state.move_failure_streak = 0
    state.last_dialogue_at = 0
    state.dialogue_escape_due_at = 0
    state.dialogue_confirm_deadline_at = 0
    state.next_dialogue_probe_at = 0
    state.next_dialogue_jump_scan_at = 0
    state.next_dialogue_jump_click_at = 0
    state.dialogue_ui_confirmed = false
    state.dialogue_ui_match = nil
    state.task_update_wait_until = 0
    state.require_task_button_refresh = false
    state.require_task_button_refresh_reason = nil
    state.task_pos_reject_until = 0
    state.task_pos_reject_reason = nil
    state.force_task_path_reacquire_until = 0
    state.force_task_path_reacquire_reason = nil
    state.force_task_path_reacquire_extra_ms = 0
    state.pause_combat_until = 0
    state.move_guard_until = 0
    state.combat_key_down = false
    state.combat_key_release_at = 0
    state.combat_mouse_down = false
    state.combat_mouse_release_at = 0
    state.cached_nearest_npc = nil
    state.cached_npc_error = nil
    state.cached_task_monsters = nil
    state.cached_task_monster_error = nil
    state.cached_task_objective_button = nil
    state.cached_task_objective_button_error = nil
    state.cached_task_objective_button_key = nil
    state.next_task_objective_button_scan_at = 0
    state.next_task_objective_button_click_at = 0
    state.cached_player_info = nil
    state.cached_player_hp = nil
    state.cached_player_max_hp = nil
    state.cached_player_hp_ratio = nil
    state.cached_player_hp_source = nil
    state.cached_player_max_hp_source = nil
    state.last_player_info_at = 0
    state.last_known_player_x = nil
    state.last_known_player_y = nil
    state.last_known_player_z = nil
    state.loading_transition_reacquire_pending = false
    state.loading_transition_reacquire_reason = nil
    state.loading_transition_reacquire_origin_x = nil
    state.loading_transition_reacquire_origin_y = nil
    state.loading_transition_reacquire_origin_z = nil
    state.loading_transition_reacquire_origin_map_name = nil
    state.loading_transition_reacquire_armed_at = 0
    state.next_potion_watch_at = 0
    state.current_task_name = nil
    state.current_task_detail = nil
    state.current_task_name_source = nil
    state.current_task_detail_source = nil
    state.current_task_name_updated_at = 0
    state.current_task_detail_updated_at = 0
    state.last_task_panel_task_name = nil
    state.last_task_panel_task_detail = nil
    state.last_task_panel_updated_at = 0
    state.last_task_panel_entry = nil
    state.last_main_task_call_started_at = 0
    state.last_main_task_call_stage = nil
    state.last_main_task_call_queries = nil
    state.last_main_task_call_phase = nil
    state.last_main_task_call_result = nil
    state.last_main_task_call_detail = nil
    state.last_main_task_call_elapsed_ms = 0
    state.last_main_task_call_nav = nil
    state.last_main_task_call_ui = nil
    M.publish_current_task_name()
    state.terminal_task_locked_name = nil
    state.terminal_task_locked_detail = nil
    state.terminal_task_locked_objective_key = nil
    state.next_map_info_refresh_at = 0
    state.cached_map_ui = nil
    state.current_map_name = nil
    state.last_map_info_error = nil
    state.map_transition_triggered = {}
    state.next_interaction_prompt_scan_at = 0
    state.cached_interaction_prompt_target = nil
    state.cached_interaction_prompt_error = nil
    state.next_exit_portal_scan_at = 0
    state.cached_exit_portal_target = nil
    state.cached_exit_portal_error = nil
    state.pending_interaction_origin = nil
    state.pending_interaction_label = nil
    state.pending_interaction_refresh_on_timeout = false
    state.npc_dialogue_combat_retry_active = false
    state.npc_dialogue_combat_retry_source = nil
    state.npc_dialogue_combat_retry_task_name = nil
    state.npc_dialogue_combat_retry_task_detail = nil
    state.npc_dialogue_combat_retry_npc_label = nil
    state.npc_dialogue_combat_retry_route_action_key = nil
    state.npc_dialogue_combat_retry_point_x = nil
    state.npc_dialogue_combat_retry_point_y = nil
    state.npc_dialogue_combat_retry_point_z = nil
    state.npc_dialogue_combat_retry_search_radius = nil
    state.npc_dialogue_combat_retry_interact_radius = nil
    state.npc_dialogue_combat_retry_move_interval_ms = nil
    state.npc_dialogue_combat_retry_deadline_at = 0
    state.npc_dialogue_combat_retry_next_retry_at = 0
    state.npc_dialogue_combat_retry_combat_seen = false
    M.clear_task_dialogue_flow_state()
    M.clear_post_dialogue_flow_state()
    state.last_potion_at = 0
    state.last_potion_q_at = 0
    state.last_potion_e_at = 0
    state.revive_started_at = 0
    state.revive_clicked_at = 0
    state.revive_click_count = 0
    state.revive_resume_ready_at = 0
    state.next_revive_click_at = 0
    state.next_task_name_probe_at = 0
    state.revive_reentry_pending = false
    state.revive_reentry_map_name = nil
    state.revive_reentry_cfg = nil
    state.revive_reentry_source = nil
    state.revive_reentry_objective_key = nil
    state.revive_reentry_deadline_at = 0
    state.post_revive_boss_engage_until = 0
    state.startup_boss_engage_until = 0
    state.startup_state_resolve_until = 0
    state.startup_main_task_reacquired = false
    state.startup_task_path_reacquire_until = 0
    state.nav_worker_task_id = 0
    state.nav_worker_share_prefix = nil
    state.nav_worker_target_version = 0
    state.nav_worker_target_signature = nil
    state.nav_worker_paused = true
    state.nav_worker_last_status = nil
    state.nav_worker_last_issue_at = 0
    state.nav_worker_last_error = nil
    state.nav_worker_next_check_at = 0
    state.nav_worker_force_direct = false
    state.nav_worker_target_published_at = 0
    state.nav_worker_target_path_index = 0
    state.nav_worker_path_route_signature = nil
    state.nav_worker_path_route_version = 0
    state.nav_worker_path_route_window_start = 0
    state.nav_worker_path_route_window_end = 0
    state.nav_worker_path_route_direction = 1
    state.nav_worker_path_route_path_signature = nil
    state.last_path_signature = nil
    state.path_direction_sign = nil
    state.last_selected_path_index = nil
    state.task_path = nil
    state.task_path_count = 0
    state.task_path_raw_count = 0
    state.task_pos = nil
    state.task_target = nil
    state.task_path_route = nil
    state.task_path_compress_mode = nil
    state.task_path_refresh_requested = true
    state.task_combat_started_at = 0
    state.task_combat_last_seen_at = 0
    state.task_combat_last_count = 0
    state.task_combat_locked_task_name = nil
    state.task_combat_locked_task_detail = nil
    state.task_combat_locked_objective_key = nil
    state.task_combat_locked_reentry_cfg = nil
    state.task_combat_locked_reentry_source = nil
    state.last_boss_context_at = 0
    state.last_boss_locked_task_name = nil
    state.last_boss_locked_task_detail = nil
    state.last_boss_locked_objective_key = nil
    state.last_boss_locked_reentry_cfg = nil
    state.last_boss_locked_reentry_source = nil
    state.task_combat_anchor_x = nil
    state.task_combat_anchor_y = nil
    state.task_combat_anchor_z = nil
    state.task_combat_kite_phase = 0
    state.task_combat_next_kite_switch_at = 0
    state.task_combat_kite_points = nil
    state.task_combat_kite_index = 0
    state.task_combat_kite_template_points = nil
    state.task_combat_kite_switch_ms = nil
    state.task_combat_kite_seamless = false
    state.task_combat_kite_async_worker = false
    state.task_combat_kite_route_worker_signature = nil
    state.task_combat_kite_route_worker_version = 0
    state.task_combat_kite_route_worker_active = false
    state.task_combat_kite_arrive_distance = nil
    state.task_combat_kite_move_interval_ms = nil
    state.task_combat_kite_force_move = false
    state.task_combat_kite_anchor_route_x = nil
    state.task_combat_kite_anchor_route_y = nil
    state.task_combat_kite_anchor_route_z = nil
    state.task_reached_unresolved_since = 0
    state.next_global_task_portal_scan_at = 0
    state.next_global_task_portal_click_at = 0
    state.global_task_portal_guard_until = 0
    state.global_task_portal_guard_reason = nil
    state.global_task_portal_wait_reacquire = false
    state.global_task_portal_reacquire_reason = nil
    state.route_point_action_board_guard_until = 0
    state.route_point_action_board_guard_key = nil
    state.route_point_action_dialogue_active_key = nil
    state.route_point_action_dialogue_active_state_key = nil
    state.route_point_action_dialogue_entered_at = 0
    state.route_point_action_dialogue_next_interact_at = 0
    state.route_point_action_dialogue_deadline_at = 0
    state.route_point_action_objective_active_key = nil
    state.route_point_action_objective_active_state_key = nil
    state.route_point_action_objective_entered_at = 0
    state.route_point_action_objective_next_probe_at = 0
    state.route_point_action_objective_deadline_at = 0
    state.route_point_action_route_active_key = nil
    state.route_point_action_route_active_state_key = nil
    state.route_point_action_route_started_at = 0
    state.route_point_action_route_deadline_at = 0
    state.route_point_action_route_index = 0
    state.route_point_action_route_next_retry_at = 0
    state.next_route_point_action_level_probe_at = 0
    state.cached_route_point_action_player_level = nil
    state.cached_route_point_action_player_level_progress = nil
    state.cached_route_point_action_player_level_text = nil
    state.cached_route_point_action_player_level_error = nil
    state.next_route_point_action_map_probe_at = 0
    state.cached_route_point_action_map_name = nil
    state.cached_route_point_action_map_error = nil
    M.clear_post_combat_loot_state()
    M.clear_task_entry_action_state()
    state.next_route_point_action_scan_at = 0
    state.next_route_point_dialogue_scan_at = 0
    M.clear_route_point_action_board_state()
    M.clear_route_point_action_npc_dialogue_state()
    M.clear_route_point_action_objective_state()
    M.clear_route_point_action_route_state()
    M.clear_route_point_action_route_wait_state()
    state.stall_retry_count = 0
    state.ticks = 0
    state.last_logs = {}
    state.route_point_action_attempted = {}
    clear_task_target_state()
    if type(M._leveling_treasure) == "table" and type(M._leveling_treasure.reset_state) == "function" then
        M._leveling_treasure.reset_state(state)
    end
end

reset_state()

local function logger(ctx)
    if type(ctx) == "table" and type(ctx.log) == "table" then
        return ctx.log
    end
    return log
end

local function task_api()
    if type(task) == "table" then
        return task
    end
    return nil
end

local function share_api(ctx)
    local sys_api = type(ctx) == "table" and ctx.sys or sys
    if type(sys_api) == "table"
        and type(sys_api.set_share) == "function"
        and type(sys_api.get_share) == "function"
    then
        return sys_api
    end
    return nil
end

local function nav_worker_key(suffix)
    if type(state.nav_worker_share_prefix) ~= "string" or state.nav_worker_share_prefix == "" then
        return nil
    end
    return state.nav_worker_share_prefix .. ":" .. tostring(suffix or "")
end

local function nav_worker_set(ctx, suffix, value)
    local sys_api = share_api(ctx)
    local key = nav_worker_key(suffix)
    if not sys_api or not key then
        return false
    end
    sys_api.set_share(key, value)
    return true
end

local function nav_worker_get(ctx, suffix)
    local sys_api = share_api(ctx)
    local key = nav_worker_key(suffix)
    if not sys_api or not key then
        return nil
    end
    return sys_api.get_share(key)
end

local function nav_worker_clear_shares(ctx)
    local sys_api = share_api(ctx)
    if not sys_api or type(state.nav_worker_share_prefix) ~= "string" or state.nav_worker_share_prefix == "" then
        return
    end

    local suffixes = {
        "stop",
        "paused",
        "target_version",
        "target_x",
        "target_y",
        "target_source",
        "target_path_index",
        "move_interval_ms",
        "mode",
        "route_version",
        "route_count",
        "route_arrive_distance",
        "route_switch_ms",
        "route_direction",
        "route_stuck_skip_ms",
        "route_progress_reset_distance",
        "worker_status",
        "last_error",
        "last_issue_at",
        "last_target_x",
        "last_target_y",
        "last_target_version",
        "heartbeat_at"
    }

    for _, suffix in ipairs(suffixes) do
        sys_api.set_share(state.nav_worker_share_prefix .. ":" .. suffix, nil)
    end
    for index = 1, M.NAV_WORKER_ROUTE_POINT_SHARE_LIMIT do
        sys_api.set_share(state.nav_worker_share_prefix .. ":route_point_" .. index .. "_x", nil)
        sys_api.set_share(state.nav_worker_share_prefix .. ":route_point_" .. index .. "_y", nil)
        sys_api.set_share(state.nav_worker_share_prefix .. ":route_point_" .. index .. "_z", nil)
        sys_api.set_share(state.nav_worker_share_prefix .. ":route_point_" .. index .. "_index", nil)
    end
end

local function nav_worker_target_signature(target)
    if type(target) ~= "table" then
        return nil
    end

    return table.concat({
        tostring(target.source or ""),
        tostring(math.floor((tonumber(target.x) or 0) * 10 + 0.5)),
        tostring(math.floor((tonumber(target.y) or 0) * 10 + 0.5)),
        tostring(tonumber(target.path_index) or 0)
    }, "|")
end

local function now_ms(ctx)
    local sys_api = type(ctx) == "table" and ctx.sys or sys
    if type(sys_api) == "table" and type(sys_api.time) == "function" then
        return sys_api.time()
    end
    return 0
end

local function sleep_ms(ctx, delay_ms)
    local sys_api = type(ctx) == "table" and ctx.sys or sys
    if type(sys_api) == "table" and type(sys_api.sleep) == "function" then
        sys_api.sleep(math.max(0, tonumber(delay_ms) or 0))
    end
end

local function safe_call(fn, ...)
    if type(fn) ~= "function" then
        return false, "target is not callable"
    end
    return pcall(fn, ...)
end

local function as_number(value)
    if type(value) == "number" then
        return value
    end
    if type(value) == "string" then
        return tonumber(value)
    end
    return nil
end

local function trim(value)
    return tostring(value or ""):gsub("^%s+", ""):gsub("%s+$", "")
end

local function normalize_monster_name(value)
    local text = trim(value)
    if text == "" then
        return nil
    end
    text = text:gsub("%s+", "")
    if text == "" then
        return nil
    end
    return text
end

local function is_force_kite_monster_name(value)
    local target_name = normalize_monster_name(value)
    if target_name == nil or type(M.FORCE_KITE_MONSTER_NAMES) ~= "table" then
        return false, target_name
    end

    if M.FORCE_KITE_MONSTER_NAMES[target_name] == true then
        return true, target_name
    end

    for candidate_name, enabled in pairs(M.FORCE_KITE_MONSTER_NAMES) do
        if enabled == true and normalize_monster_name(candidate_name) == target_name then
            return true, candidate_name
        end
    end

    return false, target_name
end

local function distance_2d(x1, y1, x2, y2)
    local dx = (tonumber(x1) or 0) - (tonumber(x2) or 0)
    local dy = (tonumber(y1) or 0) - (tonumber(y2) or 0)
    return math.sqrt(dx * dx + dy * dy)
end

local function is_valid_world_point(point)
    if type(point) ~= "table" then
        return false
    end

    local x = tonumber(point.x)
    local y = tonumber(point.y)
    if x == nil or y == nil then
        return false
    end

    if math.abs(x) < 0.001 and math.abs(y) < 0.001 then
        return false
    end

    return true
end

function M.format_world_point_for_log(point)
    if type(point) ~= "table" then
        return tostring(point)
    end

    return string.format(
        "x=%s y=%s z=%s source=%s raw=%s",
        tostring(point.x),
        tostring(point.y),
        tostring(point.z),
        tostring(point.source or ""),
        type(point.raw) == "table" and string.format(
            "x=%s y=%s z=%s",
            tostring(point.raw.x or point.raw.X or point.raw.posX or point.raw.PosX),
            tostring(point.raw.y or point.raw.Y or point.raw.posY or point.raw.PosY),
            tostring(point.raw.z or point.raw.Z or point.raw.posZ or point.raw.PosZ)
        ) or tostring(point.raw or "")
    )
end

local function log_throttled(ctx, key, level, interval_ms, message)
    local current_time = now_ms(ctx)
    local last_time = tonumber(state.last_logs[key]) or 0
    if current_time - last_time < (tonumber(interval_ms) or LOG_THROTTLE_MS) then
        return
    end

    state.last_logs[key] = current_time
    local log_api = logger(ctx)
    local log_fn = type(log_api[level]) == "function" and log_api[level] or log_api.info
    log_fn(message)
end

local function nav_api(ctx)
    if type(ctx) == "table" and type(ctx.nav) == "table" then
        return ctx.nav
    end
    return nav
end

function M.nav_debug_state_text(ctx)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) == "table" and type(nav_mod.debug_state) == "function" then
        return tostring(nav_mod.debug_state() or "")
    end
    return ""
end

function M.format_last_main_task_call_debug(current_time)
    current_time = tonumber(current_time) or 0
    local started_at = tonumber(state.last_main_task_call_started_at) or 0
    local age_ms = started_at > 0 and math.max(0, current_time - started_at) or -1
    return string.format(
        "stage=%s phase=%s result=%s age_ms=%s elapsed_ms=%s queries=%s detail=%s nav=%s ui=%s",
        tostring(state.last_main_task_call_stage or ""),
        tostring(state.last_main_task_call_phase or ""),
        tostring(state.last_main_task_call_result or ""),
        age_ms >= 0 and tostring(age_ms) or "",
        tostring(tonumber(state.last_main_task_call_elapsed_ms) or 0),
        tostring(state.last_main_task_call_queries or ""),
        tostring(state.last_main_task_call_detail or ""),
        tostring(state.last_main_task_call_nav or ""),
        tostring(state.last_main_task_call_ui or "")
    )
end

local function ensure_nav_ready(ctx, current_time)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" then
        return false, "nav module is unavailable."
    end

    if type(nav_mod.is_initialized) == "function" and nav_mod.is_initialized() then
        return true
    end

    current_time = tonumber(current_time) or now_ms(ctx)
    if current_time < (tonumber(state.next_nav_retry_at) or 0) then
        return false, state.last_nav_error or "nav init is waiting for retry."
    end

    state.next_nav_retry_at = current_time + NAV_RETRY_INTERVAL_MS

    local init_ok, init_err
    if type(nav_mod.ensure_initialized) == "function" then
        init_ok, init_err = nav_mod.ensure_initialized(
            type(ctx) == "table" and ctx.process_name or nil,
            type(ctx) == "table" and ctx.runtime_mode or nil
        )
    elseif type(nav_mod.init) == "function" then
        init_ok, init_err = nav_mod.init(
            type(ctx) == "table" and ctx.process_name or nil,
            type(ctx) == "table" and ctx.runtime_mode or nil
        )
    else
        init_ok, init_err = false, "nav init entry is unavailable."
    end

    if init_ok then
        state.last_nav_error = nil
        state.next_input_prepare_at = 0
        return true
    end

    local nav_detail = M.nav_debug_state_text(ctx)
    state.last_nav_error = tostring(init_err or "nav init failed.")
    if nav_detail ~= "" then
        state.last_nav_error = state.last_nav_error .. " | nav=" .. nav_detail
    end
    return false, state.last_nav_error
end

local function start_nav_worker(ctx, current_time, force_enabled)
    if LEVELING_USE_NAV_WORKER ~= true and force_enabled ~= true then
        state.nav_worker_force_direct = true
        return false, "leveling nav worker disabled."
    end

    local task_mod = task_api()
    local sys_api = share_api(ctx)
    if type(task_mod) ~= "table" or type(task_mod.run) ~= "function" or not sys_api then
        state.nav_worker_force_direct = true
        return false, "task.run or sys share API is unavailable."
    end

    if tonumber(state.nav_worker_task_id) and tonumber(state.nav_worker_task_id) > 0 then
        return true
    end

    current_time = tonumber(current_time) or now_ms(ctx)
    local current_task_id = type(task_mod.id) == "function" and tonumber(task_mod.id()) or 0
    state.nav_worker_share_prefix = string.format(
        "%s_%d_%d",
        NAV_WORKER_SHARE_PREFIX_BASE,
        tonumber(current_time) or 0,
        tonumber(current_task_id) or 0
    )
    state.nav_worker_target_version = 0
    state.nav_worker_target_signature = nil
    state.nav_worker_paused = true
    state.nav_worker_last_status = nil
    state.nav_worker_last_issue_at = 0
    state.nav_worker_last_error = nil
    state.nav_worker_next_check_at = current_time + NAV_WORKER_CHECK_INTERVAL_MS
    state.nav_worker_force_direct = false
    nav_worker_clear_shares(ctx)
    nav_worker_set(ctx, "stop", false)
    nav_worker_set(ctx, "paused", true)
    nav_worker_set(ctx, "target_version", 0)
    nav_worker_set(ctx, "move_interval_ms", MOVE_INTERVAL_MS)
    nav_worker_set(ctx, "mode", "target")

    local worker_id = task_mod.run("scripts/AvePointLevelingNavWorker.lua", {
        name = "AvePointLevelingNavWorker",
        priority = "high",
        share_prefix = tostring(state.nav_worker_share_prefix),
        process_name = tostring(type(ctx) == "table" and ctx.process_name or ""),
        runtime_mode = tostring(type(ctx) == "table" and ctx.runtime_mode or "")
    })
    if type(worker_id) ~= "number" or worker_id <= 0 then
        state.nav_worker_share_prefix = nil
        state.nav_worker_force_direct = true
        return false, "task.run returned invalid worker id."
    end

    state.nav_worker_task_id = worker_id
    logger(ctx).info(string.format(
        "[Leveling] nav worker started | task_id=%d share=%s",
        worker_id,
        tostring(state.nav_worker_share_prefix)
    ))
    return true
end

local function stop_nav_worker(ctx)
    nav_worker_set(ctx, "stop", true)
    nav_worker_set(ctx, "paused", true)

    local task_mod = task_api()
    local worker_id = tonumber(state.nav_worker_task_id) or 0
    if worker_id > 0 and type(task_mod) == "table" and type(task_mod.stop) == "function" then
        pcall(task_mod.stop, worker_id)
    end
    if type(task_mod) == "table" and type(task_mod.cleanup) == "function" then
        pcall(task_mod.cleanup)
    end

    nav_worker_clear_shares(ctx)
    state.nav_worker_task_id = 0
    state.nav_worker_share_prefix = nil
    state.nav_worker_target_version = 0
    state.nav_worker_target_signature = nil
    state.nav_worker_paused = true
    state.nav_worker_last_status = nil
    state.nav_worker_last_issue_at = 0
    state.nav_worker_last_error = nil
    state.nav_worker_next_check_at = 0
    state.nav_worker_force_direct = false
    state.nav_worker_target_published_at = 0
    state.nav_worker_target_path_index = 0
    state.nav_worker_path_route_signature = nil
    state.nav_worker_path_route_version = 0
    state.nav_worker_path_route_window_start = 0
    state.nav_worker_path_route_window_end = 0
    state.nav_worker_path_route_direction = 1
    state.nav_worker_path_route_path_signature = nil
end

local function ensure_nav_worker_running(ctx, current_time, force_enabled)
    if state.nav_worker_force_direct == true and force_enabled ~= true then
        return false, "nav worker direct fallback is enabled."
    end

    current_time = tonumber(current_time) or now_ms(ctx)
    if current_time < (tonumber(state.nav_worker_next_check_at) or 0)
        and tonumber(state.nav_worker_task_id) and tonumber(state.nav_worker_task_id) > 0
    then
        return true
    end

    state.nav_worker_next_check_at = current_time + NAV_WORKER_CHECK_INTERVAL_MS

    local task_mod = task_api()
    local worker_id = tonumber(state.nav_worker_task_id) or 0
    if worker_id <= 0 then
        return start_nav_worker(ctx, current_time, force_enabled)
    end

    if type(task_mod) == "table" and type(task_mod.status) == "function" then
        local status = task_mod.status(worker_id)
        if status ~= nil
            and status ~= "running"
            and status ~= "pending"
            and status ~= "paused"
        then
            local info = type(task_mod.info) == "function" and task_mod.info(worker_id) or nil
            local detail = type(info) == "table" and info.error or status
            log_throttled(ctx, "nav_worker_restart", "warn", LOG_THROTTLE_MS,
                "[Leveling] nav worker stopped unexpectedly, restarting: " .. tostring(detail))
            stop_nav_worker(ctx)
            state.nav_worker_next_check_at = current_time + NAV_WORKER_RESTART_INTERVAL_MS
            return start_nav_worker(ctx, current_time, force_enabled)
        end
    end

    local heartbeat_at = tonumber(nav_worker_get(ctx, "heartbeat_at")) or 0
    if heartbeat_at > 0 and current_time - heartbeat_at > NAV_WORKER_HEARTBEAT_STALE_MS then
        log_throttled(ctx, "nav_worker_stale", "warn", LOG_THROTTLE_MS,
            "[Leveling] nav worker heartbeat stale, restarting.")
        stop_nav_worker(ctx)
        state.nav_worker_next_check_at = current_time + NAV_WORKER_RESTART_INTERVAL_MS
        return start_nav_worker(ctx, current_time, force_enabled)
    end

    return true
end

local function sync_nav_worker_feedback(ctx, current_time)
    if LEVELING_USE_NAV_WORKER ~= true
        and (tonumber(state.nav_worker_task_id) == nil or tonumber(state.nav_worker_task_id) <= 0)
    then
        return
    end

    current_time = tonumber(current_time) or now_ms(ctx)
    if tonumber(state.nav_worker_task_id) == nil or tonumber(state.nav_worker_task_id) <= 0 then
        return
    end

    local worker_status = tostring(nav_worker_get(ctx, "worker_status") or "")
    if worker_status ~= "" and worker_status ~= tostring(state.nav_worker_last_status or "") then
        state.nav_worker_last_status = worker_status
        log_throttled(ctx, "nav_worker_status_" .. worker_status, "info", LOG_THROTTLE_MS,
            "[Leveling] nav worker status | " .. worker_status)
    end

    local last_issue_at = tonumber(nav_worker_get(ctx, "last_issue_at")) or 0
    if last_issue_at > (tonumber(state.nav_worker_last_issue_at) or 0) then
        state.nav_worker_last_issue_at = last_issue_at
        state.last_move_call_at = last_issue_at
        state.move_guard_until = math.max(
            tonumber(state.move_guard_until) or 0,
            last_issue_at + MOVE_COMBAT_GUARD_MS
        )
        log_throttled(ctx, "move_call_issued", "info", 1500, string.format(
            "[Leveling] MoveTo issued | target=%.2f, %.2f source=%s path_index=%d interval_ms=%d via=nav_worker worker_mode=%s route_index=%d/%d route_dir=%d route_distance=%.2f route_point=%.2f, %.2f original_index=%d",
            tonumber(nav_worker_get(ctx, "last_target_x")) or 0,
            tonumber(nav_worker_get(ctx, "last_target_y")) or 0,
            tostring(nav_worker_get(ctx, "target_source") or ""),
            tonumber(nav_worker_get(ctx, "target_path_index")) or 0,
            tonumber(nav_worker_get(ctx, "move_interval_ms")) or 0,
            tostring(nav_worker_get(ctx, "last_route_mode") or ""),
            tonumber(nav_worker_get(ctx, "last_route_index")) or 0,
            tonumber(nav_worker_get(ctx, "last_route_count")) or 0,
            tonumber(nav_worker_get(ctx, "last_route_direction")) or 0,
            tonumber(nav_worker_get(ctx, "last_route_distance")) or 0,
            tonumber(nav_worker_get(ctx, "last_route_point_x")) or 0,
            tonumber(nav_worker_get(ctx, "last_route_point_y")) or 0,
            tonumber(nav_worker_get(ctx, "last_route_original_index")) or 0
        ))
    end

    local last_error = trim(nav_worker_get(ctx, "last_error") or "")
    if last_error ~= "" and last_error ~= tostring(state.nav_worker_last_error or "") then
        state.nav_worker_last_error = last_error
        log_throttled(ctx, "nav_worker_last_error", "warn", LOG_THROTTLE_MS,
            "[Leveling] nav worker error: " .. tostring(last_error))
    elseif last_error == "" then
        state.nav_worker_last_error = nil
    end
end

local function hold_navigation(ctx, current_time, reason)
    current_time = tonumber(current_time) or now_ms(ctx)
    if tonumber(state.nav_worker_task_id) and tonumber(state.nav_worker_task_id) > 0 then
        nav_worker_set(ctx, "paused", true)
        state.nav_worker_paused = true
    end
    state.next_move_at = current_time
    state.last_move_call_at = 0
    if reason then
        log_throttled(ctx, "nav_hold_" .. tostring(reason), "info", LOG_THROTTLE_MS,
            "[Leveling] navigation hold | reason=" .. tostring(reason))
    end
end

function M.pause_task_combat_kite_route_worker(ctx)
    if state.task_combat_kite_route_worker_active ~= true then
        return
    end
    nav_worker_set(ctx, "paused", true)
    nav_worker_set(ctx, "mode", "target")
    state.nav_worker_paused = true
    state.task_combat_kite_route_worker_active = false
    state.task_combat_kite_route_worker_signature = nil
end

local function read_player_pos(ctx)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.player_pos) ~= "function" then
        return nil, nil, nil, "nav.player_pos is unavailable."
    end
    return nav_mod.player_pos()
end

local function read_player_info(ctx)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.player_info) ~= "function" then
        return nil, "nav.player_info is unavailable."
    end
    return nav_mod.player_info()
end

local function extract_player_hp_values(info)
    if type(info) ~= "table" then
        return nil, nil, nil, nil
    end

    local hp, hp_source = nil, nil
    if type(avepoint_extract_player_hp) == "function" then
        hp, hp_source = avepoint_extract_player_hp(info)
    end

    local max_hp, max_hp_source = nil, nil
    if type(avepoint_find_named_number) == "function" then
        max_hp, max_hp_source = avepoint_find_named_number(info, {
            "maxHp", "MaxHp", "maxHP", "MaxHP",
            "maximumHp", "MaximumHp", "maximumHP", "MaximumHP",
            "maxHealth", "MaxHealth", "maximumHealth", "MaximumHealth",
            "maxLife", "MaxLife", "maximumLife", "MaximumLife",
            "maxBlood", "MaxBlood", "maximumBlood", "MaximumBlood",
            "\u{6700}\u{5927}\u{8840}\u{91CF}", "\u{8840}\u{91CF}\u{4E0A}\u{9650}"
        }, 4)
    end

    if hp == nil or max_hp == nil or max_hp <= 0 then
        return hp, max_hp, nil, hp_source or max_hp_source
    end

    return hp, max_hp, hp / max_hp, string.format("%s/%s", tostring(hp_source or ""), tostring(max_hp_source or ""))
end

local function refresh_player_status(ctx, current_time, force_refresh)
    current_time = tonumber(current_time) or now_ms(ctx)
    if force_refresh ~= true and current_time < (tonumber(state.next_player_info_refresh_at) or 0) then
        return state.cached_player_info, state.cached_player_hp, state.cached_player_max_hp, state.cached_player_hp_ratio
    end

    state.next_player_info_refresh_at = current_time + PLAYER_INFO_REFRESH_INTERVAL_MS

    local info, info_err = read_player_info(ctx)
    if type(info) ~= "table" then
        log_throttled(ctx, "player_info_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] player info unavailable: " .. tostring(info_err))
        return state.cached_player_info, state.cached_player_hp, state.cached_player_max_hp, state.cached_player_hp_ratio
    end

    state.cached_player_info = info
    state.last_player_info_at = current_time
    state.cached_player_hp, state.cached_player_max_hp, state.cached_player_hp_ratio, state.cached_player_hp_source =
        extract_player_hp_values(info)
    return state.cached_player_info, state.cached_player_hp, state.cached_player_max_hp, state.cached_player_hp_ratio
end

local function normalize_map_name(value)
    if type(M._leveling_policy) == "table"
        and type(M._leveling_policy.normalize_map_name) == "function"
    then
        return M._leveling_policy.normalize_map_name(value)
    end
    local text = trim(value)
    if text == "" then
        return nil
    end
    return text
end

function M.normalize_task_title_key(value)
    local text = normalize_map_name(value)
    if text == nil then
        return nil
    end
    text = trim(text:gsub("^主线%s*", ""))
    text = trim(text:gsub("^涓荤嚎%s*", ""))
    text = trim(text:gsub("^涓荤窔%s*", ""))
    text = trim(text:gsub("^Main%s*Quest%s*[:锛?-]*%s*", ""))
    if text == "" then
        return nil
    end
    return text
end

M.TASK_PANEL_ENTRY_CACHE_FRESH_MS = 1800

function M.copy_task_panel_entry(entry)
    if type(entry) ~= "table" then
        return nil
    end
    return {
        title = trim(entry.title or ""),
        raw_text = trim(entry.raw_text or ""),
        kind = trim(entry.kind or ""),
        detail = trim(entry.detail or ""),
        detail_texts = type(entry.detail_texts) == "table" and entry.detail_texts or nil,
        detail_debug_candidates = type(entry.detail_debug_candidates) == "table" and entry.detail_debug_candidates or nil,
        title_addr = tonumber(entry.title_addr) or entry.title_addr,
        title_name = tostring(entry.title_name or ""),
        title_fullname = tostring(entry.title_fullname or ""),
        button_addr = tonumber(entry.button_addr) or entry.button_addr,
        button_x = tonumber(entry.button_x) or tonumber(entry.x),
        button_y = tonumber(entry.button_y) or tonumber(entry.y),
        button_name = tostring(entry.button_name or ""),
        button_fullname = tostring(entry.button_fullname or ""),
        button_kind = tostring(entry.button_kind or ""),
        button_anchor_score = tonumber(entry.button_anchor_score) or 0,
        x = tonumber(entry.x),
        y = tonumber(entry.y)
    }
end

function M.remember_task_panel_entry(entry, current_time)
    local cached = M.copy_task_panel_entry(entry)
    if type(cached) ~= "table" then
        return nil
    end
    state.last_task_panel_entry = cached
    state.last_task_panel_task_name = trim(cached.title ~= "" and cached.title or cached.raw_text)
    state.last_task_panel_task_detail = trim(cached.detail or "")
    state.last_task_panel_updated_at = tonumber(current_time) or 0
    return cached
end

function M.get_cached_task_panel_entry(current_time, preferred_task_name)
    local cached = state.last_task_panel_entry
    if type(cached) ~= "table" then
        return nil
    end
    local updated_at = tonumber(state.last_task_panel_updated_at) or 0
    if updated_at <= 0
        or ((tonumber(current_time) or 0) - updated_at) > (tonumber(M.TASK_PANEL_ENTRY_CACHE_FRESH_MS) or 1800)
    then
        return nil
    end

    local preferred_key = M.normalize_task_title_key(preferred_task_name)
    local title_key = M.normalize_task_title_key(cached.title ~= "" and cached.title or cached.raw_text)
    local detail_key = normalize_map_name(cached.detail)
    local kind = tostring(cached.kind or "")

    if preferred_key == nil then
        return M.copy_task_panel_entry(cached)
    end

    if title_key ~= nil and (
        title_key == preferred_key
        or title_key:find(preferred_key, 1, true) ~= nil
        or preferred_key:find(title_key, 1, true) ~= nil
    ) then
        return M.copy_task_panel_entry(cached)
    end

    if detail_key ~= nil and (
        detail_key == preferred_key
        or detail_key:find(preferred_key, 1, true) ~= nil
        or preferred_key:find(detail_key, 1, true) ~= nil
    ) then
        return M.copy_task_panel_entry(cached)
    end

    if kind:find("主线", 1, true) ~= nil or kind:find("涓荤嚎", 1, true) ~= nil then
        return M.copy_task_panel_entry(cached)
    end

    return nil
end

local function should_use_map_runtime_detection()
    return M.ENABLE_MAP_RUNTIME_DETECTION == true
end

function M.is_known_map_name(value)
    local target_name = normalize_map_name(value)
    if target_name == nil then
        return false
    end

    if target_name == normalize_map_name(state.current_map_name) then
        return true
    end

    if type(MAP_TASK_CONFIGS) ~= "table" then
        return false
    end

    for map_name, _ in pairs(MAP_TASK_CONFIGS) do
        if normalize_map_name(map_name) == target_name then
            return true
        end
    end

    return false
end

local function refresh_current_map_info(ctx, current_time)
    if not should_use_map_runtime_detection() then
        state.current_map_name = nil
        state.cached_map_ui = nil
        state.last_map_info_error = nil
        return nil, nil, nil
    end

    current_time = tonumber(current_time) or now_ms(ctx)
    if current_time < (tonumber(state.next_map_info_refresh_at) or 0) then
        return state.current_map_name, state.cached_map_ui, state.last_map_info_error
    end

    state.next_map_info_refresh_at = current_time + MAP_INFO_REFRESH_INTERVAL_MS

    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.get_map_ui_info) ~= "function" then
        state.last_map_info_error = "nav.get_map_ui_info is unavailable."
        return state.current_map_name, state.cached_map_ui, state.last_map_info_error
    end

    local map_ui, map_err = nav_mod.get_map_ui_info()
    if type(map_ui) ~= "table" then
        state.last_map_info_error = tostring(map_err or "map ui unavailable.")
        return state.current_map_name, state.cached_map_ui, state.last_map_info_error
    end

    state.cached_map_ui = map_ui
    state.last_map_info_error = nil

    local next_map_name = normalize_map_name(
        type(map_ui.current_map) == "table" and map_ui.current_map.text or nil
    )

    if next_map_name ~= nil and next_map_name ~= tostring(state.current_map_name or "") then
        local previous_map_name = normalize_map_name(state.current_map_name)
        state.current_map_name = next_map_name
        if state.revive_reentry_pending == true
            and normalize_map_name(state.revive_reentry_map_name) ~= next_map_name
        then
            state.revive_reentry_pending = false
            state.revive_reentry_map_name = nil
        end
        state.map_transition_triggered = {}
        clear_runtime_objective_caches()
        logger(ctx).info(string.format(
            "[Leveling] current map updated | map=%s previous=%s",
            tostring(next_map_name),
            tostring(previous_map_name or "")
        ))
    elseif next_map_name ~= nil then
        state.current_map_name = next_map_name
    end

    return state.current_map_name, state.cached_map_ui, nil
end

local function current_map_task_config()
    if not should_use_map_runtime_detection() then
        return nil, nil
    end

    if type(M._leveling_policy) == "table"
        and type(M._leveling_policy.current_map_task_config) == "function"
    then
        return M._leveling_policy.current_map_task_config(state.current_map_name, MAP_TASK_CONFIGS)
    end
    local map_name = normalize_map_name(state.current_map_name)
    if map_name == nil then
        return nil
    end
    return MAP_TASK_CONFIGS[map_name], map_name
end

function M.current_task_runtime_config()
    local task_name = normalize_map_name(state.current_task_name)
    local task_detail = normalize_map_name(state.current_task_detail)
    local function cfg_matches_runtime(cfg)
        if type(cfg) ~= "table" then
            return false
        end
        return M.matches_task_constraints(cfg, {
            task_name = task_name,
            task_detail = task_detail
        })
    end

    if type(M._leveling_policy) == "table"
        and type(M._leveling_policy.current_task_config) == "function"
    then
        local cfg, matched_name, matched_source = M._leveling_policy.current_task_config(
            state.current_task_name,
            state.current_task_detail,
            M.TASK_NAME_CONFIGS
        )
        if cfg_matches_runtime(cfg) then
            return cfg, matched_name, matched_source
        end

        if task_detail ~= nil then
            local detail_cfg, detail_name, detail_source = M._leveling_policy.current_task_config(
                nil,
                task_detail,
                M.TASK_NAME_CONFIGS
            )
            if cfg_matches_runtime(detail_cfg) then
                return detail_cfg, detail_name, detail_source
            end
        end

        return nil, task_name or task_detail, matched_source
    end

    if task_name ~= nil and type(M.TASK_NAME_CONFIGS) == "table" then
        local direct = M.TASK_NAME_CONFIGS[task_name]
        if cfg_matches_runtime(direct) then
            return direct, task_name
        end
    end
    if task_detail ~= nil and type(M.TASK_NAME_CONFIGS) == "table" then
        local detail_direct = M.TASK_NAME_CONFIGS[task_detail]
        if cfg_matches_runtime(detail_direct) then
            return detail_direct, task_detail
        end
    end
    return nil, task_name or task_detail
end

function M.matches_task_constraints(cfg, opts)
    if type(cfg) ~= "table" then
        return false
    end

    opts = type(opts) == "table" and opts or {}
    local task_key = normalize_map_name(opts.task_name or M.current_task_log_name() or state.current_task_name)
    local task_detail_key = normalize_map_name(opts.task_detail or M.current_task_log_detail() or state.current_task_detail)

    local function contains_normalized_match(list, value)
        if type(list) ~= "table" or value == nil then
            return false
        end
        for _, item in ipairs(list) do
            local item_key = normalize_map_name(item)
            if item_key ~= nil and item_key == value then
                return true
            end
        end
        return false
    end

    local function contains_normalized_substring(list, value)
        if type(list) ~= "table" or value == nil then
            return false
        end
        for _, item in ipairs(list) do
            local item_key = normalize_map_name(item)
            if item_key ~= nil and value:find(item_key, 1, true) then
                return true
            end
        end
        return false
    end

    local has_task_constraints = type(cfg.task_names) == "table" or type(cfg.task_patterns) == "table"
    local has_detail_constraints = type(cfg.task_detail_names) == "table" or type(cfg.task_detail_patterns) == "table"
    local constraint_mode = tostring(cfg.constraint_mode or cfg.match_mode or "")
    if not has_task_constraints and not has_detail_constraints then
        return true
    end

    if task_key == nil and task_detail_key == nil and cfg.allow_when_task_unknown == true then
        return true
    end

    if contains_normalized_substring(cfg.exclude_task_patterns, task_key)
        or contains_normalized_substring(cfg.exclude_task_detail_patterns, task_detail_key)
    then
        return false
    end

    local task_allowed = contains_normalized_match(cfg.task_names, task_key)
        or contains_normalized_substring(cfg.task_patterns, task_key)
    local task_detail_allowed = contains_normalized_match(cfg.task_detail_names, task_detail_key)
        or contains_normalized_substring(cfg.task_detail_patterns, task_detail_key)

    if has_task_constraints and has_detail_constraints then
        if constraint_mode == "all" then
            return task_allowed and task_detail_allowed
        end
        return task_allowed or task_detail_allowed
    end
    if has_task_constraints then
        return task_allowed
    end
    return task_detail_allowed
end

function M.current_task_entry_action_config()
    if type(state.task_entry_action_locked_cfg) == "table"
        and (tonumber(state.task_entry_action_button_click_at) or 0) > 0
    then
        return state.task_entry_action_locked_cfg, state.task_entry_action_locked_task_name
    end

    local task_cfg, task_name, task_cfg_source = M.current_task_runtime_config()
    if type(task_cfg) == "table" and type(task_cfg.entry_action) == "table" then
        return task_cfg.entry_action, task_name
    end
    return nil, task_name
end

function M.current_task_dialogue_flow_config()
    local task_cfg, task_name = M.current_task_runtime_config()
    local flow = type(task_cfg) == "table" and type(task_cfg.dialogue_flow) == "table" and task_cfg.dialogue_flow or nil
    if type(flow) ~= "table" or flow.enabled == false then
        return nil, task_name
    end
    if type(flow.steps) ~= "table" or #flow.steps <= 0 then
        return nil, task_name
    end
    return flow, task_name
end

function M.current_task_post_dialogue_flow_config()
    local task_cfg, task_name = M.current_task_runtime_config()
    local flow = type(task_cfg) == "table" and type(task_cfg.post_dialogue_flow) == "table"
        and task_cfg.post_dialogue_flow
        or nil
    if type(flow) ~= "table" or flow.enabled == false then
        return nil, task_name
    end
    if type(flow.steps) ~= "table" or #flow.steps <= 0 then
        return nil, task_name
    end
    return flow, task_name
end

function M.is_task_entry_action_active(current_time)
    local entry_action, task_name = M.current_task_entry_action_config()
    if type(entry_action) ~= "table" or tostring(entry_action.mode or "") ~= "world_map_send" then
        return false, entry_action, task_name
    end

    if type(state.task_target) == "table" then
        return false, entry_action, task_name
    end

    local armed_at = tonumber(state.task_entry_action_button_click_at) or 0
    if armed_at <= 0 then
        return false, entry_action, task_name
    end

    local timeout_ms = math.max(5000, tonumber(entry_action.timeout_ms) or 12000)
    if type(current_time) == "number" and current_time - armed_at > timeout_ms then
        return false, entry_action, task_name
    end

    return true, entry_action, task_name
end

function M.current_task_retry_call_config()
    local task_cfg, task_name = M.current_task_runtime_config()
    if type(task_cfg) == "table" and type(task_cfg.retry_call_task) == "table" then
        local retry_cfg = task_cfg.retry_call_task
        if retry_cfg.enabled ~= false then
            return retry_cfg, task_name
        end
    end
    return nil, task_name
end

function M.should_preserve_current_task_name(current_time, candidate, source)
    local current_task_cfg, current_task_name = M.current_task_runtime_config()
    local current_objective = type(current_task_cfg) == "table" and current_task_cfg.objective or nil
    if type(current_objective) ~= "table" or tostring(current_objective.mode or "") ~= "boss_kite" then
        state.boss_soft_task_change_candidate = nil
        state.boss_soft_task_change_first_at = 0
        state.boss_soft_task_change_seen_count = 0
        state.boss_soft_task_change_confirmed_candidate = nil
        state.boss_soft_task_change_confirmed_at = 0
        return false, nil
    end

    local candidate_name = normalize_map_name(candidate)
    if candidate_name == nil then
        return false, nil
    end

    local candidate_cfg = nil
    if type(M._leveling_policy) == "table"
        and type(M._leveling_policy.current_task_config) == "function"
    then
        candidate_cfg = select(1, M._leveling_policy.current_task_config(candidate_name, M.TASK_NAME_CONFIGS))
    elseif type(M.TASK_NAME_CONFIGS) == "table" then
        candidate_cfg = M.TASK_NAME_CONFIGS[candidate_name]
    end
    if candidate_cfg ~= nil then
        return false, nil
    end

    local candidate_source = tostring(source or "")
    local current_map_name = normalize_map_name(state.current_map_name)
    local sticky_window_active = state.task_combat_force_kite == true
        or (tonumber(state.startup_boss_engage_until) or 0) > (tonumber(current_time) or 0)
        or (tonumber(state.post_revive_boss_engage_until) or 0) > (tonumber(current_time) or 0)
        or tostring(state.stage or "") == "task_reached"
        or tostring(state.stage or "") == "task_combat"
        or tostring(state.stage or "") == "task_combat_kite"
        or tostring(state.stage or "") == "revive"
        or tostring(state.stage or "") == "wait_task"
        or tostring(state.stage or "") == "wait_task_update"
        or tostring(state.stage or "") == "click_task_button"
        or tostring(state.stage or "") == "wait_task_path_after_button"
        or tostring(state.stage or "") == "global_task_portal"
        or tostring(state.stage or "") == "refresh_task_button_after_dialogue"
        or tostring(state.stage or "") == "refresh_task_button_follow"
        or state.require_task_button_refresh == true
        or (tonumber(state.task_update_wait_until) or 0) > (tonumber(current_time) or 0)
    if sticky_window_active ~= true then
        state.boss_soft_task_change_candidate = nil
        state.boss_soft_task_change_first_at = 0
        state.boss_soft_task_change_seen_count = 0
        return false, nil
    end

    if candidate_source == "nearby_text" then
        local candidate_title_key = M.normalize_task_title_key(candidate_name)
        local current_state_title_key = M.normalize_task_title_key(state.current_task_name)
        local current_log_title_key = M.normalize_task_title_key(M.current_task_log_name())
        local current_cfg_title_key = M.normalize_task_title_key(current_task_name)
        local current_detail_key = M.normalize_task_title_key(M.current_task_log_detail() or state.current_task_detail)
        local locked_task_key = M.normalize_task_title_key(state.task_combat_locked_task_name)
        local locked_detail_key = M.normalize_task_title_key(state.task_combat_locked_task_detail)
        local same_current_context = candidate_title_key ~= nil and (
            candidate_title_key == current_state_title_key
            or candidate_title_key == current_log_title_key
            or candidate_title_key == current_cfg_title_key
            or candidate_title_key == current_detail_key
            or candidate_title_key == locked_task_key
            or candidate_title_key == locked_detail_key
        )
        if same_current_context then
            state.boss_soft_task_change_candidate = nil
            state.boss_soft_task_change_first_at = 0
            state.boss_soft_task_change_seen_count = 0
            return true, string.format(
                "boss_objective_sticky_nearby_same_context current=%s candidate=%s stage=%s",
                tostring(current_task_name or ""),
                tostring(candidate_name or ""),
                tostring(state.stage or "")
            )
        end

        if current_objective.allow_nearby_text_task_change_exit == true then
            local confirm_ms = math.max(300, tonumber(current_objective.nearby_text_task_change_confirm_ms) or 1500)
            local min_count = math.max(1, tonumber(current_objective.nearby_text_task_change_confirm_count) or 2)
            local pending_key = M.normalize_task_title_key(state.boss_soft_task_change_candidate)
            local candidate_matches_pending = pending_key ~= nil
                and candidate_title_key ~= nil
                and pending_key == candidate_title_key
            if not candidate_matches_pending then
                state.boss_soft_task_change_candidate = candidate_name
                state.boss_soft_task_change_first_at = current_time
                state.boss_soft_task_change_seen_count = 1
                state.boss_soft_task_change_confirmed_candidate = nil
                state.boss_soft_task_change_confirmed_at = 0
                return true, string.format(
                    "boss_objective_sticky_nearby_pending current=%s candidate=%s confirm_ms=%d count=1/%d stage=%s",
                    tostring(current_task_name or ""),
                    tostring(candidate_name or ""),
                    confirm_ms,
                    min_count,
                    tostring(state.stage or "")
                )
            end

            state.boss_soft_task_change_seen_count = (tonumber(state.boss_soft_task_change_seen_count) or 1) + 1
            local first_at = tonumber(state.boss_soft_task_change_first_at) or current_time
            local elapsed = math.max(0, current_time - first_at)
            if elapsed >= confirm_ms and (tonumber(state.boss_soft_task_change_seen_count) or 0) >= min_count then
                state.boss_soft_task_change_confirmed_candidate = candidate_name
                state.boss_soft_task_change_confirmed_at = current_time
                return false, nil
            end

            return true, string.format(
                "boss_objective_sticky_nearby_pending current=%s candidate=%s elapsed=%d/%dms count=%d/%d stage=%s",
                tostring(current_task_name or ""),
                tostring(candidate_name or ""),
                elapsed,
                confirm_ms,
                tonumber(state.boss_soft_task_change_seen_count) or 0,
                min_count,
                tostring(state.stage or "")
            )
        end

        return true, string.format(
            "boss_objective_sticky_nearby_unconfirmed current=%s candidate=%s stage=%s",
            tostring(current_task_name or ""),
            tostring(candidate_name or ""),
            tostring(state.stage or "")
        )
    end

    local generic_mainline = candidate_name:match("^主线%s+") ~= nil
    local looks_like_current_map = current_map_name ~= nil and candidate_name == current_map_name
    local looks_like_known_map = M.is_known_map_name(candidate_name)
    if generic_mainline or looks_like_current_map or looks_like_known_map then
        return true, string.format(
            "boss_objective_sticky current=%s candidate=%s stage=%s",
            tostring(current_task_name or ""),
            tostring(candidate_name or ""),
            tostring(state.stage or "")
        )
    end

    return false, nil
end

function M.current_objective_point_config(destination, target)
    if type(M.OBJECTIVE_POINT_CONFIGS) ~= "table" then
        return nil
    end

    local objective_point = nil
    if is_valid_world_point(destination) then
        objective_point = destination
    elseif is_valid_world_point(state.task_pos) then
        objective_point = state.task_pos
    elseif is_valid_world_point(target) then
        objective_point = target
    end

    if not is_valid_world_point(objective_point) then
        return nil
    end

    for _, cfg in ipairs(M.OBJECTIVE_POINT_CONFIGS) do
        local point_x = tonumber(cfg and cfg.x)
        local point_y = tonumber(cfg and cfg.y)
        local point_radius = tonumber(cfg and cfg.radius) or 0
        if point_x ~= nil and point_y ~= nil and point_radius > 0 then
            local point_distance = distance_2d(objective_point.x, objective_point.y, point_x, point_y)
            if point_distance <= point_radius then
                if not M.matches_task_constraints(cfg) then
                    goto continue
                end
                return cfg
            end
        end
        ::continue::
    end

    return nil
end

function M.find_objective_point_config_by_key(key)
    local lookup_key = normalize_map_name(key)
    if lookup_key == nil or type(M.OBJECTIVE_POINT_CONFIGS) ~= "table" then
        return nil
    end

    for _, cfg in ipairs(M.OBJECTIVE_POINT_CONFIGS) do
        local cfg_key = normalize_map_name(type(cfg) == "table" and cfg.key or nil)
        if cfg_key ~= nil and cfg_key == lookup_key then
            return cfg
        end
    end
    return nil
end

function M.extract_revive_reentry_config(cfg)
    if type(cfg) == "table" and type(cfg.revive_reentry) == "table" then
        return cfg.revive_reentry
    end
    return nil
end

function M.lock_boss_combat_context(task_name, objective_cfg, point_objective_cfg)
    state.task_combat_locked_task_name = trim(task_name or M.current_task_log_name())
    state.task_combat_locked_task_detail = trim(M.current_task_log_detail() or state.current_task_detail)
    state.task_combat_locked_objective_key = tostring(
        type(objective_cfg) == "table" and objective_cfg.key
            or type(point_objective_cfg) == "table" and point_objective_cfg.key
            or ""
    )

    local locked_reentry_cfg = M.extract_revive_reentry_config(point_objective_cfg)
        or M.extract_revive_reentry_config(objective_cfg)
    state.task_combat_locked_reentry_cfg = locked_reentry_cfg
    state.task_combat_locked_reentry_source = type(point_objective_cfg) == "table"
            and locked_reentry_cfg == M.extract_revive_reentry_config(point_objective_cfg)
            and "point"
        or (type(objective_cfg) == "table" and locked_reentry_cfg ~= nil and "objective" or nil)
    state.last_boss_context_at = now_ms(nil)
    state.last_boss_locked_task_name = state.task_combat_locked_task_name
    state.last_boss_locked_task_detail = state.task_combat_locked_task_detail
    state.last_boss_locked_objective_key = state.task_combat_locked_objective_key
    state.last_boss_locked_reentry_cfg = locked_reentry_cfg
    state.last_boss_locked_reentry_source = state.task_combat_locked_reentry_source
end

function M.resolve_revive_reentry_config()
    if type(state.task_combat_locked_reentry_cfg) == "table" then
        return state.task_combat_locked_reentry_cfg,
            tostring(state.task_combat_locked_reentry_source or "locked"),
            tostring(state.task_combat_locked_objective_key or "")
    end

    local task_cfg = select(1, M.current_task_runtime_config())
    local objective_cfg = type(task_cfg) == "table" and type(task_cfg.objective) == "table" and task_cfg.objective or nil
    if type(objective_cfg) == "table" and type(objective_cfg.revive_reentry) == "table" then
        return objective_cfg.revive_reentry, "task", tostring(objective_cfg.key or "")
    end

    local locked_point_cfg = M.find_objective_point_config_by_key(state.task_combat_locked_objective_key)
    if type(locked_point_cfg) == "table" and type(locked_point_cfg.revive_reentry) == "table" then
        return locked_point_cfg.revive_reentry, "locked_point", tostring(locked_point_cfg.key or "")
    end

    local sticky_context_at = tonumber(state.last_boss_context_at) or 0
    if sticky_context_at > 0 and now_ms(nil) - sticky_context_at <= 45000 then
        if type(state.last_boss_locked_reentry_cfg) == "table" then
            return state.last_boss_locked_reentry_cfg,
                tostring(state.last_boss_locked_reentry_source or "sticky"),
                tostring(state.last_boss_locked_objective_key or "")
        end

        local sticky_point_cfg = M.find_objective_point_config_by_key(state.last_boss_locked_objective_key)
        if type(sticky_point_cfg) == "table" and type(sticky_point_cfg.revive_reentry) == "table" then
            return sticky_point_cfg.revive_reentry, "sticky_point", tostring(sticky_point_cfg.key or "")
        end
    end

    local map_cfg, map_name = current_map_task_config()
    if type(map_cfg) == "table" and type(map_cfg.revive_reentry) == "table" then
        return map_cfg.revive_reentry, "map", tostring(map_name or "")
    end

    return nil, nil, nil
end

function M.maybe_handle_boss_task_change(ctx, current_time)
    if state.task_combat_force_kite ~= true
        and tostring(state.stage or "") ~= "task_combat_kite"
        and tostring(state.stage or "") ~= "task_combat"
        and tostring(state.stage or "") ~= "post_combat_loot"
    then
        return false
    end

    if current_time < (tonumber(state.next_task_name_probe_at) or 0) then
        return false
    end

    local hint_x = tonumber(MAIN_TASK_BUTTON_STEP and MAIN_TASK_BUTTON_STEP.hint_client_x)
    local hint_y = tonumber(MAIN_TASK_BUTTON_STEP and MAIN_TASK_BUTTON_STEP.hint_client_y)
    if hint_x ~= nil and hint_y ~= nil then
        M.refresh_current_task_name(ctx, current_time, nil, hint_x, hint_y)
    end
    state.next_task_name_probe_at = current_time + 900

    local locked_objective_key = normalize_map_name(state.task_combat_locked_objective_key)
    local locked_task_name = normalize_map_name(state.task_combat_locked_task_name)
    local locked_task_detail = normalize_map_name(state.task_combat_locked_task_detail)
    local current_task_cfg, current_task_name = M.current_task_runtime_config()
    local current_objective = type(current_task_cfg) == "table" and type(current_task_cfg.objective) == "table" and current_task_cfg.objective or nil
    local current_objective_key = normalize_map_name(type(current_objective) == "table" and current_objective.key or nil)
    local locked_point_cfg = M.find_objective_point_config_by_key(state.task_combat_locked_objective_key)
    local current_log_task_name = normalize_map_name(M.current_task_log_name())
    local current_log_task_detail = normalize_map_name(M.current_task_log_detail())
    local current_task_source = tostring(state.current_task_name_source or "")
    local current_detail_source = tostring(state.current_task_detail_source or "")
    local detail_changed = locked_task_detail ~= nil
        and current_log_task_detail ~= nil
        and current_log_task_detail ~= locked_task_detail
    local detail_missing_exit_enabled = (
        type(current_objective) == "table"
        and current_objective.exit_kite_on_detail_missing == true
    ) or (
        type(locked_point_cfg) == "table"
        and locked_point_cfg.exit_kite_on_detail_missing == true
    )
    local detail_missing_exit_after_ms = math.max(
        0,
        tonumber(type(current_objective) == "table" and current_objective.exit_kite_on_detail_missing_after_ms)
            or tonumber(type(locked_point_cfg) == "table" and locked_point_cfg.exit_kite_on_detail_missing_after_ms)
            or 0
    )
    local combat_alive_ms = math.max(
        0,
        current_time - (tonumber(state.task_combat_started_at) or current_time)
    )
    local detail_missing_changed = detail_missing_exit_enabled
        and locked_task_detail ~= nil
        and current_log_task_detail == nil
        and combat_alive_ms >= detail_missing_exit_after_ms
        and current_log_task_name ~= nil
        and (
            current_log_task_name == locked_task_name
            or current_log_task_name == locked_task_detail
        )

    local name_changed = current_log_task_name ~= nil
        and current_log_task_name ~= locked_task_name
        and current_objective_key ~= locked_objective_key
    local soft_probe_change = (current_task_source == "nearby_text" and name_changed)
        or (current_detail_source == "nearby_text" and detail_changed)
    if locked_objective_key ~= nil
        and (name_changed or detail_changed or detail_missing_changed)
    then
        if soft_probe_change then
            log_throttled(ctx, "boss_task_change_soft_probe_ignored", "warn", LOG_THROTTLE_MS, string.format(
                "[Leveling] boss task change ignored from soft text probe | old_task=%s old_detail=%s old_objective=%s new_task=%s new_detail=%s new_objective=%s task_source=%s detail_source=%s stage=%s",
                tostring(locked_task_name or ""),
                tostring(locked_task_detail or ""),
                tostring(locked_objective_key or ""),
                tostring(current_log_task_name or current_task_name or ""),
                tostring(current_log_task_detail or ""),
                tostring(current_objective_key or ""),
                tostring(current_task_source or ""),
                tostring(current_detail_source or ""),
                tostring(state.stage or "")
            ))
            return false
        end
        logger(ctx).info(string.format(
            "[Leveling] boss task changed, exit kite | old_task=%s old_detail=%s old_objective=%s new_task=%s new_detail=%s new_objective=%s stage=%s",
            tostring(locked_task_name or ""),
            tostring(locked_task_detail or ""),
            tostring(locked_objective_key or ""),
            tostring(current_log_task_name or current_task_name or ""),
            tostring(current_log_task_detail or ""),
            tostring(current_objective_key or ""),
            tostring(state.stage or "")
        ))
        clear_task_combat_state()
        clear_runtime_objective_caches()
        schedule_task_refresh_after_transition(ctx, current_time, "boss_task_changed", POST_DIALOGUE_SETTLE_MS, {
            force_task_call = true,
            task_pos_reject_extra_ms = 2000
        })
        return true
    end

    return false
end

function M.ensure_terminal_task_lock(task_name, objective_cfg)
    if trim(state.terminal_task_locked_name) == "" then
        local lock_name = trim(task_name or M.current_task_log_name())
        if lock_name ~= "" then
            state.terminal_task_locked_name = lock_name
        end
    end

    if trim(state.terminal_task_locked_detail) == "" then
        local lock_detail = trim(M.current_task_log_detail() or state.current_task_detail)
        if lock_detail ~= "" then
            state.terminal_task_locked_detail = lock_detail
        end
    end

    if trim(state.terminal_task_locked_objective_key) == "" then
        local objective_key = trim(type(objective_cfg) == "table" and objective_cfg.key or "")
        if objective_key ~= "" then
            state.terminal_task_locked_objective_key = objective_key
        end
    end
end

function M.maybe_handle_terminal_task_change(ctx, current_time)
    local stage_name = tostring(state.stage or "")
    local terminal_stage_active = stage_name == "task_reached"
        or stage_name == "task_combat"
        or stage_name == "task_combat_kite"
        or stage_name == "task_combat_settle"
        or stage_name == "task_combat_complete_settle"
        or stage_name == "task_spawn_wait"
        or stage_name == "approach_task_objective"
        or stage_name == "interaction_prompt"
        or stage_name == "npc_dialogue"
    if not terminal_stage_active then
        return false
    end

    if current_time < (tonumber(state.next_task_name_probe_at) or 0) then
        return false
    end

    local hint_x = tonumber(MAIN_TASK_BUTTON_STEP and MAIN_TASK_BUTTON_STEP.hint_client_x)
    local hint_y = tonumber(MAIN_TASK_BUTTON_STEP and MAIN_TASK_BUTTON_STEP.hint_client_y)
    if hint_x ~= nil and hint_y ~= nil then
        M.refresh_current_task_name(ctx, current_time, nil, hint_x, hint_y)
    end
    state.next_task_name_probe_at = current_time + 900

    local locked_task_name = normalize_map_name(state.terminal_task_locked_name)
    local locked_task_detail = normalize_map_name(state.terminal_task_locked_detail)
    local locked_objective_key = normalize_map_name(state.terminal_task_locked_objective_key)
    local current_task_cfg, current_task_name = M.current_task_runtime_config()
    local current_objective = type(current_task_cfg) == "table" and type(current_task_cfg.objective) == "table"
        and current_task_cfg.objective or nil
    local current_objective_key = normalize_map_name(type(current_objective) == "table" and current_objective.key or nil)
    local current_log_task_name = normalize_map_name(M.current_task_log_name())
    local current_log_task_detail = normalize_map_name(M.current_task_log_detail())
    local function normalize_task_title_drift_key(value)
        local raw = normalize_map_name(value)
        if raw == nil then
            return nil
        end
        raw = raw:gsub("^主线%s*", "")
        return raw ~= "" and raw or nil
    end

    local task_changed = locked_task_name ~= nil
        and current_log_task_name ~= nil
        and current_log_task_name ~= locked_task_name
    local detail_changed = locked_task_detail ~= nil
        and current_log_task_detail ~= nil
        and current_log_task_detail ~= locked_task_detail
    local objective_changed = locked_objective_key ~= nil
        and current_objective_key ~= nil
        and current_objective_key ~= locked_objective_key
    local locked_task_title_key = normalize_task_title_drift_key(locked_task_name)
    local current_task_title_key = normalize_task_title_drift_key(current_log_task_name)
    local task_title_prefix_only_drift = locked_task_title_key ~= nil
        and current_task_title_key ~= nil
        and locked_task_title_key == current_task_title_key
    local prefix_only_title_drift = task_changed
        and not detail_changed
        and not objective_changed
        and task_title_prefix_only_drift

    if prefix_only_title_drift then
        log_throttled(ctx, "terminal_prefix_only_title_drift", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] terminal task prefix drift ignored | old_task=%s new_task=%s old_detail=%s new_detail=%s stage=%s",
            tostring(state.terminal_task_locked_name or ""),
            tostring(M.current_task_log_name() or current_task_name or ""),
            tostring(state.terminal_task_locked_detail or ""),
            tostring(M.current_task_log_detail() or ""),
            stage_name
        ))
        return false
    end

    if locked_objective_key ~= nil
        and current_objective_key ~= nil
        and current_objective_key == locked_objective_key
        and type(current_objective) == "table"
        and current_objective.ignore_terminal_text_change_when_objective_same == true
        and (task_changed or detail_changed)
    then
        log_throttled(ctx, "terminal_text_change_objective_same", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] terminal task text drift ignored; objective unchanged | old_task=%s old_detail=%s objective=%s new_task=%s new_detail=%s stage=%s",
            tostring(state.terminal_task_locked_name or ""),
            tostring(state.terminal_task_locked_detail or ""),
            tostring(state.terminal_task_locked_objective_key or ""),
            tostring(M.current_task_log_name() or current_task_name or ""),
            tostring(M.current_task_log_detail() or ""),
            stage_name
        ))
        return false
    end

    local detail_stable_title_drift_stage = stage_name == "task_reached"
        or stage_name == "interaction_prompt"
    local detail_stable_title_drift = detail_stable_title_drift_stage
        and task_changed
        and not detail_changed
        and not objective_changed
        and locked_task_detail ~= nil
        and current_log_task_detail ~= nil
        and current_log_task_detail == locked_task_detail
        and (
            locked_task_name == locked_task_detail
            or current_log_task_name == locked_task_detail
            or task_title_prefix_only_drift
        )
    if detail_stable_title_drift then
        log_throttled(ctx, "terminal_detail_stable_title_drift", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] terminal task title drift ignored; detail unchanged | old_task=%s old_detail=%s new_task=%s new_detail=%s stage=%s",
            tostring(state.terminal_task_locked_name or ""),
            tostring(state.terminal_task_locked_detail or ""),
            tostring(M.current_task_log_name() or current_task_name or ""),
            tostring(M.current_task_log_detail() or ""),
            stage_name
        ))
        return false
    end

    if not task_changed and not detail_changed and not objective_changed then
        return false
    end

    logger(ctx).info(string.format(
        "[Leveling] task changed, resume next step | old_task=%s old_detail=%s old_objective=%s new_task=%s new_detail=%s new_objective=%s stage=%s",
        tostring(state.terminal_task_locked_name or ""),
        tostring(state.terminal_task_locked_detail or ""),
        tostring(state.terminal_task_locked_objective_key or ""),
        tostring(M.current_task_log_name() or current_task_name or ""),
        tostring(M.current_task_log_detail() or ""),
        tostring(current_objective_key or ""),
        stage_name
    ))
    clear_task_combat_state()
    clear_runtime_objective_caches()
    state.terminal_task_locked_name = nil
    state.terminal_task_locked_detail = nil
    state.terminal_task_locked_objective_key = nil
    schedule_task_refresh_after_transition(ctx, current_time, "task_changed", TASK_BUTTON_SETTLE_MS, {
        force_task_call = true,
        task_pos_reject_extra_ms = 2000
    })
    return true
end

extract_player_hp_values = function(info)
    if type(info) ~= "table" then
        return nil, nil, nil, nil
    end

    local hp, hp_source = nil, nil
    if type(avepoint_extract_player_hp) == "function" then
        hp, hp_source = avepoint_extract_player_hp(info)
    end

    local max_hp, max_hp_source = nil, nil
    if type(avepoint_find_named_number) == "function" then
        max_hp, max_hp_source = avepoint_find_named_number(info, {
            "maxHp", "MaxHp", "maxHP", "MaxHP",
            "hpMax", "HpMax", "HPMax",
            "maximumHp", "MaximumHp", "maximumHP", "MaximumHP",
            "maxHealth", "MaxHealth", "maximumHealth", "MaximumHealth",
            "healthMax", "HealthMax",
            "maxLife", "MaxLife", "maximumLife", "MaximumLife",
            "lifeMax", "LifeMax",
            "maxBlood", "MaxBlood", "maximumBlood", "MaximumBlood",
            "bloodMax", "BloodMax"
        }, 4)
    end

    if max_hp == nil and type(avepoint_find_named_number_fuzzy) == "function" then
        max_hp, max_hp_source = avepoint_find_named_number_fuzzy(info, {
            "upper", "limit", "ceiling"
        }, 4)
    end

    if hp == nil or max_hp == nil or max_hp <= 0 then
        return hp, max_hp, nil, hp_source or max_hp_source
    end

    return hp, max_hp, hp / max_hp, string.format("%s/%s", tostring(hp_source or ""), tostring(max_hp_source or ""))
end

local function read_loading_state(ctx)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.is_loading) ~= "function" then
        return false
    end

    local value, err = nav_mod.is_loading()
    if value == nil then
        log_throttled(ctx, "loading_api_error", "warn", LOG_THROTTLE_MS,
            "[Leveling] is_loading failed: " .. tostring(err))
        return false
    end

    return value == true
end

local function read_main_interface_state(ctx)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.is_main_interface) ~= "function" then
        return nil, "nav.is_main_interface is unavailable."
    end

    local value, err = nav_mod.is_main_interface()
    if value == nil then
        return nil, err or "is_main_interface failed."
    end

    return value == true, nil
end

local function extract_position_from_item(ctx, item)
    if type(item) ~= "table" then
        return nil, nil, nil
    end

    local nav_mod = nav_api(ctx)
    if type(nav_mod) == "table" and type(nav_mod.extract_position) == "function" then
        local x, y, z = nav_mod.extract_position(item)
        if x ~= nil and y ~= nil then
            return x, y, z
        end
    end

    local function pick(tbl, keys)
        if type(tbl) ~= "table" then
            return nil
        end
        for _, key in ipairs(keys) do
            local value = as_number(tbl[key])
            if value ~= nil then
                return value
            end
        end
        return nil
    end

    local x = pick(item, { "x", "X", "posX", "PosX", "worldX", "WorldX" })
    local y = pick(item, { "y", "Y", "posY", "PosY", "worldY", "WorldY" })
    local z = pick(item, { "z", "Z", "posZ", "PosZ", "worldZ", "WorldZ" })
    if x ~= nil and y ~= nil then
        return x, y, z
    end

    for _, key in ipairs({ "pos", "position", "coord", "coords", "point", "location", "Location" }) do
        local nested = item[key]
        if type(nested) == "table" then
            x = pick(nested, { "x", "X", "posX", "PosX", "worldX", "WorldX" })
            y = pick(nested, { "y", "Y", "posY", "PosY", "worldY", "WorldY" })
            z = pick(nested, { "z", "Z", "posZ", "PosZ", "worldZ", "WorldZ" })
            if x ~= nil and y ~= nil then
                return x, y, z
            end
        end
    end

    return nil, nil, nil
end

local function npc_label(item)
    if type(item) ~= "table" then
        return ""
    end

    for _, key in ipairs({
        "name", "Name", "text", "Text", "fullname", "Fullname",
        "displayName", "DisplayName", "title", "Title"
    }) do
        local value = trim(item[key])
        if value ~= "" then
            return value
        end
    end

    return ""
end

function M.monster_special_kind(item, label)
    if type(item) ~= "table" then
        return nil
    end

    local text = trim(label or npc_label(item))
    local force_kite_match = select(1, is_force_kite_monster_name(text))
    if force_kite_match then
        return "forced_kite"
    end
    local lower_text = text:lower()
    if lower_text ~= "" then
        if lower_text:find("boss", 1, true)
            or lower_text:find("leader", 1, true)
            or lower_text:find("elite", 1, true)
            or text:find("棣栭", 1, true)
            or text:find("棰嗕富", 1, true)
            or text:find("绮捐嫳", 1, true)
            or text:find("\u{7A00}\u{6709}", 1, true)
        then
            return "named"
        end
    end

    for _, key in ipairs({
        "isBoss", "IsBoss", "boss", "Boss",
        "isElite", "IsElite", "elite", "Elite",
        "isRare", "IsRare", "rare", "Rare",
        "isNamed", "IsNamed", "named", "Named",
        "rank", "Rank", "rarity", "Rarity",
        "quality", "Quality", "monsterType", "MonsterType"
    }) do
        local value = item[key]
        if value == true or value == 1 then
            return "flag:" .. tostring(key)
        end
        local value_text = trim(value)
        local lower_value = value_text:lower()
        if lower_value ~= "" then
            if lower_value:find("boss", 1, true)
                or lower_value:find("leader", 1, true)
                or lower_value:find("elite", 1, true)
                or lower_value:find("rare", 1, true)
                or lower_value:find("named", 1, true)
                or value_text:find("棣栭", 1, true)
                or value_text:find("棰嗕富", 1, true)
                or value_text:find("绮捐嫳", 1, true)
                or value_text:find("\u{7A00}\u{6709}", 1, true)
            then
                return tostring(key) .. ":" .. value_text
            end
        end
    end

    return nil
end

local function ui_item_text(item)
    if type(item) ~= "table" then
        return ""
    end

    return table.concat({
        tostring(item.name or ""),
        tostring(item.text or ""),
        tostring(item.Fullname or item.fullname or "")
    }, " "):lower()
end

local function confirm_dialogue_ui_visible(ctx, current_time)
    current_time = tonumber(current_time) or now_ms(ctx)
    if state.dialogue_ui_confirmed == true then
        return true, state.dialogue_ui_match
    end
    if current_time < (tonumber(state.next_dialogue_probe_at) or 0) then
        return false, state.dialogue_ui_match
    end

    state.next_dialogue_probe_at = current_time + DIALOGUE_UI_PROBE_INTERVAL_MS

    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.enum_ui) ~= "function" then
        state.dialogue_ui_match = "nav.enum_ui is unavailable."
        return false, state.dialogue_ui_match
    end

    local ui, ui_err = nav_mod.enum_ui()
    if type(ui) ~= "table" then
        state.dialogue_ui_match = tostring(ui_err or "enum_ui failed.")
        return false, state.dialogue_ui_match
    end

    local dialogue_patterns = DIALOGUE_UI_PATTERNS
    if tostring(state.pending_interaction_origin or "") == "interaction_prompt" then
        dialogue_patterns = {
            "dialog",
            "dialogue",
            "talk",
            "conversation",
            "story",
            "subtitle",
            "skipbutton",
            "skip",
            "cinematic",
            "movie"
        }
    end

    local function scan(items, kind, patterns)
        for _, item in ipairs(items or {}) do
            local text = ui_item_text(item)
            if text ~= "" then
                local excluded = false
                for _, pattern in ipairs(DIALOGUE_UI_EXCLUDE_PATTERNS) do
                    if text:find(pattern, 1, true) then
                        excluded = true
                        break
                    end
                end
                if excluded ~= true then
                    for _, pattern in ipairs(patterns or {}) do
                        if text:find(pattern, 1, true) then
                            return string.format("%s:%s", tostring(kind or "ui"), text)
                        end
                    end
                end
            end
        end
        return nil
    end

    local safe_matched = scan(ui.buttons, "button", DIALOGUE_ESCAPE_SAFE_PATTERNS)
        or scan(ui.texts, "text", DIALOGUE_ESCAPE_SAFE_PATTERNS)
        or scan(ui.images, "image", DIALOGUE_ESCAPE_SAFE_PATTERNS)
    if safe_matched then
        state.dialogue_ui_confirmed = true
        state.dialogue_ui_match = safe_matched
        logger(ctx).info("[Leveling] dialogue UI confirmed | match=" .. tostring(safe_matched))
        return true, safe_matched
    end

    local matched = scan(ui.buttons, "button", dialogue_patterns)
        or scan(ui.texts, "text", dialogue_patterns)
        or scan(ui.images, "image", dialogue_patterns)
    if matched then
        state.dialogue_ui_confirmed = false
        state.dialogue_ui_match = matched
        log_throttled(
            ctx,
            "dialogue_ui_not_escape_safe",
            "info",
            LOG_THROTTLE_MS,
            "[Leveling] dialogue UI seen but ESC is only allowed for skip/cinematic UI | match="
                .. tostring(matched)
        )
        return false, "dialogue UI is not escape-safe: " .. tostring(matched)
    end

    state.dialogue_ui_match = "dialogue UI pattern not found."
    return false, state.dialogue_ui_match
end

local function build_path_signature(path)
    if type(path) ~= "table" or #path == 0 then
        return ""
    end

    local first_point = path[1]
    local last_point = path[#path]
    return string.format(
        "%d|%.1f|%.1f|%.1f|%.1f",
        #path,
        tonumber(first_point and first_point.x) or 0,
        tonumber(first_point and first_point.y) or 0,
        tonumber(last_point and last_point.x) or 0,
        tonumber(last_point and last_point.y) or 0
    )
end

local function is_sharp_turn(prev_point, point, next_point)
    if type(prev_point) ~= "table" or type(point) ~= "table" or type(next_point) ~= "table" then
        return false
    end

    local ax = (tonumber(point.x) or 0) - (tonumber(prev_point.x) or 0)
    local ay = (tonumber(point.y) or 0) - (tonumber(prev_point.y) or 0)
    local bx = (tonumber(next_point.x) or 0) - (tonumber(point.x) or 0)
    local by = (tonumber(next_point.y) or 0) - (tonumber(point.y) or 0)
    local len_a = math.sqrt(ax * ax + ay * ay)
    local len_b = math.sqrt(bx * bx + by * by)
    if len_a < 1 or len_b < 1 then
        return false
    end

    local cos_value = (ax * bx + ay * by) / (len_a * len_b)
    return cos_value <= TASK_PATH_COMPRESS_TURN_COS_THRESHOLD
end

local function compress_task_path(path)
    if type(path) ~= "table" or #path == 0 then
        return nil, 0, "invalid"
    end

    local raw_count = #path
    if raw_count <= 2 then
        return path, raw_count, "raw_short"
    end

    if raw_count <= 6 then
        return path, raw_count, "raw_short"
    end

    local function compress_with_thresholds(min_distance, turn_min_distance, turn_cos_threshold)
        local function is_sharp_turn_with_threshold(prev_point, point, next_point)
            if type(prev_point) ~= "table" or type(point) ~= "table" or type(next_point) ~= "table" then
                return false
            end

            local ax = (tonumber(point.x) or 0) - (tonumber(prev_point.x) or 0)
            local ay = (tonumber(point.y) or 0) - (tonumber(prev_point.y) or 0)
            local bx = (tonumber(next_point.x) or 0) - (tonumber(point.x) or 0)
            local by = (tonumber(next_point.y) or 0) - (tonumber(point.y) or 0)
            local len_a = math.sqrt(ax * ax + ay * ay)
            local len_b = math.sqrt(bx * bx + by * by)
            if len_a < 1 or len_b < 1 then
                return false
            end

            local cos_value = (ax * bx + ay * by) / (len_a * len_b)
            return cos_value <= turn_cos_threshold
        end

        local compressed = { path[1] }
        local last_kept = path[1]

        for index = 2, raw_count - 1 do
            local point = path[index]
            local next_point = path[index + 1]
            local segment_distance = distance_2d(last_kept.x, last_kept.y, point.x, point.y)
            local keep_for_distance = segment_distance >= min_distance
            local keep_for_turn = segment_distance >= turn_min_distance
                and is_sharp_turn_with_threshold(last_kept, point, next_point)

            if keep_for_distance or keep_for_turn then
                compressed[#compressed + 1] = point
                last_kept = point
            end
        end

        local last_point = path[raw_count]
        local last_added = compressed[#compressed]
        if type(last_added) ~= "table"
            or distance_2d(last_added.x, last_added.y, last_point.x, last_point.y) >= 1
        then
            compressed[#compressed + 1] = last_point
        end

        return compressed
    end

    local compressed = compress_with_thresholds(
        TASK_PATH_COMPRESS_MIN_DISTANCE,
        TASK_PATH_COMPRESS_TURN_MIN_DISTANCE,
        TASK_PATH_COMPRESS_TURN_COS_THRESHOLD
    )
    local compress_mode = "default"
    local compressed_count = type(compressed) == "table" and #compressed or 0
    local over_compressed = raw_count >= 12
        and compressed_count <= math.max(4, math.floor(raw_count * 0.24))

    if over_compressed then
        local relaxed = compress_with_thresholds(
            math.max(420, math.floor(TASK_PATH_COMPRESS_MIN_DISTANCE * 0.55)),
            math.max(180, math.floor(TASK_PATH_COMPRESS_TURN_MIN_DISTANCE * 0.65)),
            math.min(0.94, TASK_PATH_COMPRESS_TURN_COS_THRESHOLD + 0.09)
        )
        if type(relaxed) == "table" and #relaxed > compressed_count then
            compressed = relaxed
            compressed_count = #relaxed
            compress_mode = "adaptive_relaxed"
        end

        if raw_count <= 10 and compressed_count <= math.max(3, math.floor(raw_count * 0.2)) then
            return path, raw_count, "raw_fallback"
        end
    end

    return compressed, raw_count, compress_mode
end

local function clone_task_path(path)
    if type(path) ~= "table" or #path == 0 then
        return nil, 0, "invalid"
    end

    local copy = {}
    for index, point in ipairs(path) do
        if is_valid_world_point(point) then
            copy[#copy + 1] = {
                x = tonumber(point.x),
                y = tonumber(point.y),
                z = tonumber(point.z),
                index = tonumber(point.index) or index
            }
        end
    end

    if #copy == 0 then
        return nil, 0, "invalid"
    end

    if M.TASK_PATH_USE_RAW_PATH == true then
        return copy, #copy, "raw_worker"
    end

    return compress_task_path(copy)
end

local function path_direction_name(direction)
    return direction == -1 and "reverse" or "forward"
end

local function find_nearest_path_index(player_x, player_y, path)
    if type(path) ~= "table" or #path == 0 then
        return nil, nil
    end

    local nearest_index = 1
    local nearest_distance = nil
    for index, point in ipairs(path) do
        local dist = distance_2d(player_x or point.x, player_y or point.y, point.x, point.y)
        if nearest_distance == nil or dist < nearest_distance then
            nearest_distance = dist
            nearest_index = index
        end
    end

    return nearest_index, nearest_distance
end

local function choose_path_direction(player_x, player_y, path)
    if type(path) ~= "table" or #path == 0 then
        return 1
    end

    local first_point = path[1]
    local last_point = path[#path]
    local first_distance = distance_2d(player_x, player_y, first_point.x, first_point.y)
    local last_distance = distance_2d(player_x, player_y, last_point.x, last_point.y)
    return first_distance <= last_distance and 1 or -1
end

local function advance_path_index(path, start_index, direction, player_x, player_y)
    if type(path) ~= "table" or #path == 0 then
        return nil
    end

    local selected_index = math.max(1, math.min(#path, tonumber(start_index) or 1))
    local max_advance = math.max(1, math.floor(tonumber(PATH_LOOKAHEAD_POINTS) or 1))
    for _ = 1, max_advance do
        local next_index = selected_index + direction
        if next_index < 1 or next_index > #path then
            break
        end

        selected_index = next_index
        local point = path[selected_index]
        local dist = distance_2d(player_x or point.x, player_y or point.y, point.x, point.y)
        if dist >= PATH_MIN_ADVANCE_DISTANCE then
            break
        end
    end

    return math.max(1, math.min(#path, selected_index))
end

local function step_path_index(path, current_index, direction)
    if type(path) ~= "table" or #path == 0 then
        return nil
    end

    local next_index = math.max(1, math.min(#path, tonumber(current_index) or 1))
    next_index = next_index + (tonumber(direction) or 1)
    if next_index < 1 or next_index > #path then
        return nil
    end
    return next_index
end

local function ensure_task_path_route(player_x, player_y, current_time)
    local path = state.task_path
    if type(path) ~= "table" or #path == 0 then
        state.task_path_route = nil
        return nil
    end

    local path_signature = build_path_signature(path)
    local route = state.task_path_route
    if type(route) ~= "table" or route.signature ~= path_signature then
        local nearest_index, nearest_distance = find_nearest_path_index(player_x, player_y, path)
        if nearest_index == nil then
            state.task_path_route = nil
            return nil
        end

        local direction = choose_path_direction(player_x, player_y, path)
        local selected_index = advance_path_index(path, nearest_index, direction, player_x, player_y)
        route = {
            signature = path_signature,
            direction = direction,
            index = selected_index,
            nearest_index = nearest_index,
            nearest_distance = nearest_distance,
            point_started_at = tonumber(current_time) or 0,
            point_best_distance = math.huge,
            next_repath_at = (tonumber(current_time) or 0) + TASK_PATH_REPATH_INTERVAL_MS
        }
        state.task_path_route = route
        state.last_path_signature = path_signature
        state.path_direction_sign = direction
        state.last_selected_path_index = selected_index
    end

    return route
end

local function maybe_reanchor_task_path_route(route, path, player_x, player_y, current_time)
    if type(route) ~= "table" or type(path) ~= "table" then
        return false
    end

    local nearest_index, nearest_distance = find_nearest_path_index(player_x, player_y, path)
    if nearest_index ~= nil then
        route.nearest_index = nearest_index
        route.nearest_distance = nearest_distance
    end

    local point = path[route.index]
    if type(point) ~= "table" then
        return false
    end

    if current_time < (tonumber(route.next_repath_at) or 0) then
        return false
    end

    route.next_repath_at = current_time + TASK_PATH_REPATH_INTERVAL_MS

    if nearest_index == nil or nearest_distance == nil then
        return false
    end

    local current_distance = distance_2d(player_x, player_y, point.x, point.y)
    local index_delta = math.abs((tonumber(nearest_index) or 0) - (tonumber(route.index) or 0))
    local should_reanchor = index_delta >= TASK_PATH_REANCHOR_INDEX_DELTA
        and current_distance >= TASK_PATH_REANCHOR_DISTANCE
        and nearest_distance + TASK_PATH_REANCHOR_ADVANTAGE_DISTANCE < current_distance

    if not should_reanchor then
        return false
    end

    route.index = advance_path_index(path, nearest_index, route.direction, player_x, player_y)
    route.point_started_at = current_time
    route.point_best_distance = math.huge
    state.next_move_at = 0
    state.last_selected_path_index = route.index
    return true
end

local function build_target_from_task_path_route(route, player_x, player_y)
    local path = state.task_path
    if type(route) ~= "table" or type(path) ~= "table" then
        return nil
    end

    local point = path[route.index]
    if type(point) ~= "table" then
        return nil
    end

    local current_distance = distance_2d(player_x or point.x, player_y or point.y, point.x, point.y)
    return {
        x = point.x,
        y = point.y,
        z = point.z,
        source = "task_path",
        path_index = tonumber(point.index) or tonumber(route.index) or 0,
        route_index = tonumber(route.index) or 0,
        path_points = #path,
        nearest_index = tonumber(route.nearest_index) or tonumber(route.index) or 0,
        nearest_distance = tonumber(route.nearest_distance) or tonumber(current_distance) or 0,
        current_distance = tonumber(current_distance) or 0,
        path_direction = path_direction_name(route.direction),
        move_interval_ms = M.TASK_FOLLOW_MOVE_INTERVAL_MS
    }
end

local function sync_task_path_target(ctx, player_x, player_y, current_time)
    local route = ensure_task_path_route(player_x, player_y, current_time)
    if type(route) ~= "table" then
        return nil, false, nil
    end

    local path = state.task_path
    local reanchored = maybe_reanchor_task_path_route(route, path, player_x, player_y, current_time)
    if reanchored == true then
        logger(ctx).info(string.format(
            "[Leveling] task path reanchored | route_index=%d nearest_index=%d nearest_distance=%.2f",
            tonumber(route.index) or 0,
            tonumber(route.nearest_index) or 0,
            tonumber(route.nearest_distance) or 0
        ))
    end

    local target = build_target_from_task_path_route(route, player_x, player_y)
    if type(target) ~= "table" then
        return nil, false, nil
    end

    local route_debug_signature = tostring(route.signature or "") .. "|"
        .. tostring(route.index or "") .. "|"
        .. tostring(route.nearest_index or "")
    if tostring(state.last_task_path_route_debug_signature or "") ~= route_debug_signature then
        state.last_task_path_route_debug_signature = route_debug_signature
        local first_point = path[1]
        local last_point = path[#path]
        local nearest_point = path[math.max(1, math.min(#path, tonumber(route.nearest_index) or 1))]
        local selected_point = path[math.max(1, math.min(#path, tonumber(route.index) or 1))]
        logger(ctx).info(string.format(
            "[Leveling] task path route selected | signature=%s player=%.2f, %.2f direction=%s route_index=%d nearest_index=%d nearest_distance=%.2f selected=%.2f, %.2f, %.2f nearest=%.2f, %.2f, %.2f first=%.2f, %.2f, %.2f last=%.2f, %.2f, %.2f points=%d",
            tostring(route.signature or ""),
            tonumber(player_x) or 0,
            tonumber(player_y) or 0,
            path_direction_name(route.direction),
            tonumber(route.index) or 0,
            tonumber(route.nearest_index) or 0,
            tonumber(route.nearest_distance) or 0,
            tonumber(selected_point and selected_point.x) or 0,
            tonumber(selected_point and selected_point.y) or 0,
            tonumber(selected_point and selected_point.z) or 0,
            tonumber(nearest_point and nearest_point.x) or 0,
            tonumber(nearest_point and nearest_point.y) or 0,
            tonumber(nearest_point and nearest_point.z) or 0,
            tonumber(first_point and first_point.x) or 0,
            tonumber(first_point and first_point.y) or 0,
            tonumber(first_point and first_point.z) or 0,
            tonumber(last_point and last_point.x) or 0,
            tonumber(last_point and last_point.y) or 0,
            tonumber(last_point and last_point.z) or 0,
            #path
        ))
    end

    local changed = false
    local current_distance = tonumber(target.current_distance) or distance_2d(player_x, player_y, target.x, target.y)
    local best_distance = tonumber(route.point_best_distance) or math.huge
    if current_distance < best_distance then
        route.point_best_distance = current_distance
        if best_distance == math.huge
            or current_distance <= best_distance - TASK_PATH_PROGRESS_RESET_DISTANCE
        then
            route.point_started_at = current_time
        end
    end

    if current_distance <= TASK_PATH_POINT_ARRIVE_TOLERANCE then
        local next_index = step_path_index(path, route.index, route.direction)
        if next_index ~= nil then
            route.index = next_index
            route.point_started_at = current_time
            route.point_best_distance = math.huge
            route.next_repath_at = current_time + TASK_PATH_REPATH_INTERVAL_MS
            state.next_move_at = 0
            state.last_selected_path_index = route.index
            target = build_target_from_task_path_route(route, player_x, player_y)
            changed = true
            current_distance = tonumber(target and target.current_distance) or current_distance
        end
    elseif current_time - (tonumber(route.point_started_at) or current_time) >= TASK_PATH_STUCK_SKIP_MS then
        local next_index = step_path_index(path, route.index, route.direction)
        if next_index ~= nil then
            local old_index = route.index
            route.index = next_index
            route.point_started_at = current_time
            route.point_best_distance = math.huge
            route.next_repath_at = current_time + TASK_PATH_REPATH_INTERVAL_MS
            state.next_move_at = 0
            state.last_selected_path_index = route.index
            target = build_target_from_task_path_route(route, player_x, player_y)
            changed = true
            logger(nil).warn(string.format(
                "[Leveling] task path point looked stuck, skip route_index=%d -> %d",
                tonumber(old_index) or 0,
                tonumber(route.index) or 0
            ))
        end
    end

    return target, changed, current_distance
end

local function build_target_signature(target)
    if type(target) ~= "table" then
        return ""
    end

    return string.format(
        "%s|%d|%.1f|%.1f|%.1f",
        tostring(target.source or ""),
        tonumber(target.path_index) or 0,
        tonumber(target.x) or 0,
        tonumber(target.y) or 0,
        tonumber(target.z) or 0
    )
end

local function assign_task_target(ctx, current_time, target)
    if type(target) ~= "table" then
        return false
    end

    state.task_target = target
    state.task_target_updated_at = current_time

    local signature = build_target_signature(target)
    if signature ~= state.last_task_signature then
        state.last_task_signature = signature
        state.next_move_at = 0
        logger(ctx).info(string.format(
            "[Leveling] task target updated | source=%s path_index=%d nearest_index=%d nearest_distance=%.2f order=%s path_points=%d target=%.2f, %.2f, %.2f",
            tostring(target.source or ""),
            tonumber(target.path_index) or 0,
            tonumber(target.nearest_index) or 0,
            tonumber(target.nearest_distance) or 0,
            tostring(target.path_direction or ""),
            tonumber(target.path_points) or 0,
            tonumber(target.x) or 0,
            tonumber(target.y) or 0,
            tonumber(target.z) or 0
        ))
    end

    if state.global_task_portal_wait_reacquire == true then
        state.global_task_portal_wait_reacquire = false
        local post_reacquire_guard_ms = 6000
        state.global_task_portal_guard_until = math.max(
            tonumber(state.global_task_portal_guard_until) or 0,
            current_time + post_reacquire_guard_ms
        )
        state.global_task_portal_guard_reason = "post_transition_task_reacquired"
        logger(ctx).info(string.format(
            "[Leveling] global task portal resume deferred after task reacquire | source=%s reason=%s guard_ms=%d",
            tostring(target.source or ""),
            tostring(state.global_task_portal_reacquire_reason or ""),
            post_reacquire_guard_ms
        ))
        state.global_task_portal_reacquire_reason = nil
    end

    return true
end

function M.clear_loading_transition_reacquire_state()
    state.loading_transition_reacquire_pending = false
    state.loading_transition_reacquire_reason = nil
    state.loading_transition_reacquire_origin_x = nil
    state.loading_transition_reacquire_origin_y = nil
    state.loading_transition_reacquire_origin_z = nil
    state.loading_transition_reacquire_origin_map_name = nil
    state.loading_transition_reacquire_armed_at = 0
end

function M.arm_loading_transition_reacquire(ctx, current_time, reason)
    if state.loading_transition_reacquire_pending == true then
        return
    end

    state.loading_transition_reacquire_pending = true
    state.loading_transition_reacquire_reason = tostring(reason or "loading_state")
    state.loading_transition_reacquire_origin_x = tonumber(state.last_known_player_x)
    state.loading_transition_reacquire_origin_y = tonumber(state.last_known_player_y)
    state.loading_transition_reacquire_origin_z = tonumber(state.last_known_player_z)
    state.loading_transition_reacquire_origin_map_name = normalize_map_name(state.current_map_name)
    state.loading_transition_reacquire_armed_at = tonumber(current_time) or 0
    logger(ctx).info(string.format(
        "[Leveling] loading transition recovery armed | reason=%s origin=%.2f, %.2f, %.2f map=%s",
        tostring(state.loading_transition_reacquire_reason or ""),
        tonumber(state.loading_transition_reacquire_origin_x) or 0,
        tonumber(state.loading_transition_reacquire_origin_y) or 0,
        tonumber(state.loading_transition_reacquire_origin_z) or 0,
        tostring(state.loading_transition_reacquire_origin_map_name or "")
    ))
end

function M.maybe_handle_loading_transition_reacquire(ctx, current_time, player_x, player_y, player_z)
    if state.loading_transition_reacquire_pending ~= true then
        return false
    end

    local loading_reacquire_cfg = type(M.LOADING_TRANSITION_REACQUIRE_CFG) == "table"
        and M.LOADING_TRANSITION_REACQUIRE_CFG
        or {}

    if state.require_task_button_refresh == true then
        logger(ctx).info(string.format(
            "[Leveling] loading transition recovery released to existing task refresh | reason=%s refresh_reason=%s",
            tostring(state.loading_transition_reacquire_reason or ""),
            tostring(state.require_task_button_refresh_reason or "")
        ))
        M.clear_loading_transition_reacquire_state()
        return false
    end

    local pending_force_reacquire = (tonumber(state.force_task_path_reacquire_until) or 0) > current_time
    local waiting_task_path_after_click = tonumber(state.last_task_button_click_at) ~= nil
        and (tonumber(state.last_task_button_click_at) or 0) > 0
        and current_time < math.max(
            (tonumber(state.last_task_button_click_at) or 0) + TASK_BUTTON_SETTLE_MS,
            tonumber(state.task_path_wait_until) or 0
        )
    if pending_force_reacquire
        or waiting_task_path_after_click
    then
        logger(ctx).info(string.format(
            "[Leveling] loading transition recovery released to main task pipeline | reason=%s force_reacquire=%s waiting_task_path=%s stage=%s",
            tostring(state.loading_transition_reacquire_reason or ""),
            pending_force_reacquire and "true" or "false",
            waiting_task_path_after_click and "true" or "false",
            tostring(state.stage or "")
        ))
        M.clear_loading_transition_reacquire_state()
        return false
    end

    local path = state.task_path
    local nearest_index = nil
    local nearest_distance = nil
    if type(path) == "table" and #path > 0 then
        nearest_index, nearest_distance = find_nearest_path_index(player_x, player_y, path)
    end

    local target = state.task_target
    local target_distance = type(target) == "table"
        and distance_2d(player_x, player_y, target.x, target.y)
        or nil
    local origin_distance = nil
    if type(state.loading_transition_reacquire_origin_x) == "number"
        and type(state.loading_transition_reacquire_origin_y) == "number"
    then
        origin_distance = distance_2d(
            player_x,
            player_y,
            state.loading_transition_reacquire_origin_x,
            state.loading_transition_reacquire_origin_y
        )
    end

    local origin_map_name = normalize_map_name(state.loading_transition_reacquire_origin_map_name)
    local current_map_name = normalize_map_name(state.current_map_name)
    local map_changed = origin_map_name ~= nil
        and current_map_name ~= nil
        and origin_map_name ~= current_map_name
    local path_detached = type(nearest_distance) == "number"
        and nearest_distance >= (tonumber(loading_reacquire_cfg.path_distance) or (TASK_PATH_REANCHOR_DISTANCE * 2))
    local target_detached = type(target_distance) == "number"
        and target_distance >= (tonumber(loading_reacquire_cfg.target_distance) or 2200)
    local origin_shifted = type(origin_distance) == "number"
        and origin_distance >= (tonumber(loading_reacquire_cfg.origin_distance) or 1200)
        and (type(path) == "table" and #path > 0 or type(target) == "table")

    if not (map_changed or path_detached or target_detached or origin_shifted) then
        logger(ctx).info(string.format(
            "[Leveling] loading transition recovery resumed in place | reason=%s origin_distance=%s nearest_path_distance=%s target_distance=%s map_before=%s map_now=%s",
            tostring(state.loading_transition_reacquire_reason or ""),
            type(origin_distance) == "number" and string.format("%.2f", origin_distance) or "nil",
            type(nearest_distance) == "number" and string.format("%.2f", nearest_distance) or "nil",
            type(target_distance) == "number" and string.format("%.2f", target_distance) or "nil",
            tostring(origin_map_name or ""),
            tostring(current_map_name or "")
        ))
        M.clear_loading_transition_reacquire_state()
        return false
    end

    logger(ctx).info(string.format(
        "[Leveling] loading transition recovery forcing main task reacquire | reason=%s origin_distance=%s nearest_path_distance=%s nearest_index=%s target_distance=%s map_before=%s map_now=%s pos=%.2f, %.2f, %.2f",
        tostring(state.loading_transition_reacquire_reason or ""),
        type(origin_distance) == "number" and string.format("%.2f", origin_distance) or "nil",
        type(nearest_distance) == "number" and string.format("%.2f", nearest_distance) or "nil",
        tostring(nearest_index or ""),
        type(target_distance) == "number" and string.format("%.2f", target_distance) or "nil",
        tostring(origin_map_name or ""),
        tostring(current_map_name or ""),
        tonumber(player_x) or 0,
        tonumber(player_y) or 0,
        tonumber(player_z) or 0
    ))
    M.clear_loading_transition_reacquire_state()
    schedule_task_refresh_after_transition(ctx, current_time, "map_transition_loading_recovery", tonumber(loading_reacquire_cfg.settle_ms) or 900, {
        force_task_call = true,
        task_pos_reject_extra_ms = 3500
    })
    return true
end

local function build_task_destination_point(player_x, player_y)
    if is_valid_world_point(state.task_pos) then
        return {
            x = tonumber(state.task_pos.x),
            y = tonumber(state.task_pos.y),
            z = tonumber(state.task_pos.z),
            source = "task_pos"
        }
    end

    local path = state.task_path
    if type(path) ~= "table" or #path == 0 then
        return nil
    end

    local first_point = path[1]
    local last_point = path[#path]
    if type(first_point) ~= "table" or type(last_point) ~= "table" then
        return nil
    end

    local route = state.task_path_route
    local direction = tonumber(route and route.direction)
    local destination = nil
    if direction ~= nil then
        destination = direction < 0 and first_point or last_point
    else
        local first_distance = distance_2d(player_x, player_y, first_point.x, first_point.y)
        local last_distance = distance_2d(player_x, player_y, last_point.x, last_point.y)
        destination = first_distance >= last_distance and first_point or last_point
    end

    if not is_valid_world_point(destination) then
        return nil
    end

    return {
        x = tonumber(destination.x),
        y = tonumber(destination.y),
        z = tonumber(destination.z),
        source = destination == first_point and "path_first_endpoint" or "path_last_endpoint"
    }
end

local function prepare_inputs(ctx, current_time, opts)
    opts = opts or {}
    current_time = tonumber(current_time) or now_ms(ctx)
    if current_time < (tonumber(state.next_input_prepare_at) or 0) then
        return true
    end

    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.window_hwnd) ~= "function" then
        return false, "nav.window_hwnd is unavailable."
    end

    local hwnd, hwnd_err = nav_mod.window_hwnd()
    if not hwnd then
        return false, hwnd_err or "game window not found."
    end

    local wnd_api = type(ctx) == "table" and ctx.wnd or wnd
    if type(wnd_api) == "table" and type(wnd_api.set_foreground) == "function" then
        safe_call(wnd_api.set_foreground, hwnd)
        sleep_ms(ctx, INPUT_PREPARE_SETTLE_MS)
    end

    local preferred_mode = type(ctx) == "table" and ctx.runtime_mode or "driver"
    preferred_mode = preferred_mode == "api" and "api" or "driver"

    local function set_input_mode(input_api)
        if type(input_api) ~= "table" or type(input_api.set_mode) ~= "function" then
            return
        end

        local fallback_mode = preferred_mode == "api" and "driver" or "api"
        local ok, result = safe_call(input_api.set_mode, preferred_mode)
        if not ok or result == false then
            safe_call(input_api.set_mode, fallback_mode)
        end
    end

    local keybd_api = type(ctx) == "table" and ctx.keybd or keybd
    local mouse_api = type(ctx) == "table" and ctx.mouse or mouse
    local need_mouse = opts.need_mouse ~= false

    set_input_mode(keybd_api)
    if need_mouse then
        set_input_mode(mouse_api)
    end

    if type(keybd_api) == "table" and type(keybd_api.set_window) == "function" then
        safe_call(keybd_api.set_window, hwnd)
    end
    if need_mouse and type(mouse_api) == "table" and type(mouse_api.set_window) == "function" then
        safe_call(mouse_api.set_window, hwnd)
    end

    state.next_input_prepare_at = current_time + INPUT_PREPARE_INTERVAL_MS
    return true
end

local function input_apis(ctx)
    local keybd_api = type(ctx) == "table" and ctx.keybd or keybd
    local mouse_api = type(ctx) == "table" and ctx.mouse or mouse
    return keybd_api, mouse_api
end

local function resolve_input_hwnd(ctx)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.window_hwnd) ~= "function" then
        return nil, "nav.window_hwnd is unavailable."
    end

    local hwnd, hwnd_err = nav_mod.window_hwnd()
    if not hwnd then
        return nil, hwnd_err or "game window not found."
    end

    return hwnd
end

local function try_background_key_click(ctx, vk, label)
    local hwnd, hwnd_err = resolve_input_hwnd(ctx)
    if not hwnd then
        return false, hwnd_err
    end

    local keybd_api = select(1, input_apis(ctx))
    if type(keybd_api) ~= "table" or type(keybd_api.post_click) ~= "function" then
        return false, "keybd.post_click is unavailable."
    end

    local ok, result = safe_call(keybd_api.post_click, hwnd, vk)
    if not ok or result == false then
        return false, string.format("%s keybd.post_click(0x%02X) failed.", tostring(label or "key"), tonumber(vk) or 0)
    end

    return true
end

local function try_background_key_state(ctx, vk, is_down, label)
    local hwnd, hwnd_err = resolve_input_hwnd(ctx)
    if not hwnd then
        return false, hwnd_err
    end

    local keybd_api = select(1, input_apis(ctx))
    if type(keybd_api) ~= "table" or type(keybd_api.post_key) ~= "function" then
        return false, "keybd.post_key is unavailable."
    end

    local ok, result = safe_call(keybd_api.post_key, hwnd, vk, is_down == true)
    if not ok or result == false then
        return false, string.format(
            "%s keybd.post_key(0x%02X, %s) failed.",
            tostring(label or "key"),
            tonumber(vk) or 0,
            tostring(is_down == true)
        )
    end

    return true
end

local function press_keyboard_hotkey(ctx, current_time, vk, label)
    local background_ok = try_background_key_click(ctx, vk, label)
    if background_ok then
        return true
    end

    local prepared, prepare_err = prepare_inputs(ctx, current_time, {
        need_mouse = false
    })
    if not prepared then
        return false, prepare_err
    end

    local keybd_api = select(1, input_apis(ctx))
    if type(keybd_api) ~= "table" or type(keybd_api.click) ~= "function" then
        return false, "keybd.click is unavailable."
    end

    local ok, result = safe_call(keybd_api.click, vk)
    if not ok or result == false then
        return false, string.format("%s keybd.click(0x%02X) failed.", tostring(label or "key"), tonumber(vk) or 0)
    end

    return true
end

M.try_press_potion_hotkey = function(ctx, vk, label)
    local background_ok, background_err = try_background_key_click(ctx, vk, label)
    if background_ok then
        return true, nil, "background"
    end

    local driver_api = type(ctx) == "table" and ctx.driver or driver
    if type(driver_api) == "table" and type(driver_api.keybd_click) == "function" then
        local ok, result = safe_call(driver_api.keybd_click, vk)
        if ok and result ~= false then
            return true, nil, "driver_click"
        end
    end

    local hwnd, hwnd_err = resolve_input_hwnd(ctx)
    if not hwnd then
        return false, background_err or hwnd_err or "game window not found."
    end

    local keybd_api = select(1, input_apis(ctx))
    if type(keybd_api) ~= "table" then
        return false, background_err or "keybd api unavailable."
    end

    local preferred_mode = type(ctx) == "table" and ctx.runtime_mode or "driver"
    preferred_mode = preferred_mode == "api" and "api" or "driver"
    if type(keybd_api.set_mode) == "function" then
        local fallback_mode = preferred_mode == "api" and "driver" or "api"
        local ok, result = safe_call(keybd_api.set_mode, preferred_mode)
        if not ok or result == false then
            safe_call(keybd_api.set_mode, fallback_mode)
        end
    end
    if type(keybd_api.set_window) == "function" then
        safe_call(keybd_api.set_window, hwnd)
    end
    if type(keybd_api.click) == "function" then
        local ok, result = safe_call(keybd_api.click, vk)
        if ok and result ~= false then
            return true, nil, "key_click"
        end
    end

    return false, background_err or string.format("%s nonintrusive key click failed.", tostring(label or "key"))
end

function M.try_press_right_click_nonintrusive(ctx, label)
    local hwnd, hwnd_err = resolve_input_hwnd(ctx)
    if not hwnd then
        return false, hwnd_err, nil
    end

    local nav_mod = nav_api(ctx)
    local mouse_api = select(2, input_apis(ctx))
    if type(mouse_api) == "table"
        and type(mouse_api.post_click) == "function"
        and type(nav_mod) == "table"
        and type(nav_mod.cursor_client_pos) == "function"
    then
        local cursor, cursor_err = nav_mod.cursor_client_pos()
        if type(cursor) == "table"
            and type(cursor.client_x) == "number"
            and type(cursor.client_y) == "number"
        then
            local ok, result = safe_call(
                mouse_api.post_click,
                hwnd,
                math.floor((tonumber(cursor.client_x) or 0) + 0.5),
                math.floor((tonumber(cursor.client_y) or 0) + 0.5),
                "right",
                ACTION_MOUSE_HOLD_MS
            )
            if ok and result ~= false then
                return true, nil, "mouse_post_click"
            end
        elseif cursor_err then
            hwnd_err = cursor_err
        end
    end

    if type(mouse_api) ~= "table" then
        return false, hwnd_err or "mouse api unavailable.", nil
    end

    local preferred_mode = type(ctx) == "table" and ctx.runtime_mode or "driver"
    preferred_mode = preferred_mode == "api" and "api" or "driver"
    if type(mouse_api.set_mode) == "function" then
        local fallback_mode = preferred_mode == "api" and "driver" or "api"
        local ok, result = safe_call(mouse_api.set_mode, preferred_mode)
        if not ok or result == false then
            safe_call(mouse_api.set_mode, fallback_mode)
        end
    end
    if type(mouse_api.set_window) == "function" then
        safe_call(mouse_api.set_window, hwnd)
    end
    if type(mouse_api.click) == "function" then
        local ok, result = safe_call(mouse_api.click, "right", ACTION_MOUSE_HOLD_MS)
        if ok and result ~= false then
            return true, nil, "mouse_click"
        end
    end

    return false, hwnd_err or string.format("%s right click failed.", tostring(label or "mouse")), nil
end

local function release_async_combat_inputs(ctx, current_time, force)
    current_time = tonumber(current_time) or now_ms(ctx)

    local release_key = state.combat_key_down == true
        and (force == true or current_time >= (tonumber(state.combat_key_release_at) or 0))
    local release_mouse = state.combat_mouse_down == true
        and (force == true or current_time >= (tonumber(state.combat_mouse_release_at) or 0))
    if not release_key and not release_mouse then
        return true
    end

    local keybd_api, mouse_api = input_apis(ctx)
    local all_ok = true
    local need_fallback_prepare = release_mouse == true

    if release_key then
        local released = false
        local driver_api = type(ctx) == "table" and ctx.driver or driver
        if type(driver_api) == "table" and type(driver_api.keybd_up) == "function" then
            local ok, result = safe_call(driver_api.keybd_up, VK_W)
            if ok and result ~= false then
                released = true
            end
        end
        local post_ok = false
        if not released then
            post_ok = try_background_key_state(ctx, VK_W, false, "combat release")
        end
        if released or post_ok then
            released = true
        else
            need_fallback_prepare = true
        end
        if not released and need_fallback_prepare then
            local prepared, prepare_err = prepare_inputs(ctx, current_time, {
                need_mouse = release_mouse
            })
            if not prepared then
                log_throttled(ctx, "combat_release_prepare_failed", "warn", LOG_THROTTLE_MS,
                    "[Leveling] release combat inputs prepare failed: " .. tostring(prepare_err))
                return false, prepare_err
            end

            if type(keybd_api) == "table" and type(keybd_api.up) == "function" then
                local ok, result = safe_call(keybd_api.up, VK_W)
                if ok and result ~= false then
                    released = true
                end
            end
        end
        if not released then
            all_ok = false
        end
        state.combat_key_down = false
        state.combat_key_release_at = 0
    end

    if release_mouse then
        local prepared, prepare_err = prepare_inputs(ctx, current_time, {
            need_mouse = true
        })
        if not prepared then
            log_throttled(ctx, "combat_release_prepare_failed", "warn", LOG_THROTTLE_MS,
                "[Leveling] release combat inputs prepare failed: " .. tostring(prepare_err))
            return false, prepare_err
        end

        if type(mouse_api) == "table" and type(mouse_api.up) == "function" then
            local ok, result = safe_call(mouse_api.up, "right")
            if not ok or result == false then
                all_ok = false
            end
        end
        state.combat_mouse_down = false
        state.combat_mouse_release_at = 0
    end

    if not all_ok then
        log_throttled(ctx, "combat_release_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] release combat inputs failed.")
    end
    return all_ok
end

local function issue_move_direct(ctx, current_time, target)
    if state.task_combat_kite_route_worker_active == true
        and tostring(type(target) == "table" and target.source or "") ~= "task_combat_kite"
    then
        M.pause_task_combat_kite_route_worker(ctx)
    end

    local force_move = type(target) == "table" and target.force_move == true
    if not force_move and current_time < (tonumber(state.next_move_at) or 0) then
        return true
    end

    local move_interval_ms = MOVE_INTERVAL_MS
    if type(target) == "table" and target.source == "task_path" then
        move_interval_ms = math.max(120, tonumber(target.move_interval_ms) or TASK_PATH_REPATH_INTERVAL_MS)
    elseif type(target) == "table" and target.source == "treasure_path" then
        move_interval_ms = math.max(120, tonumber(target.move_interval_ms) or M.TASK_FOLLOW_MOVE_INTERVAL_MS)
    elseif type(target) == "table" and (target.source == "task_pos" or target.source == "task_pos_precise") then
        move_interval_ms = math.max(120, tonumber(target.move_interval_ms) or MOVE_INTERVAL_MS)
    elseif type(target) == "table" and target.source == "task_combat_kite" then
        move_interval_ms = math.max(
            120,
            tonumber(target.move_interval_ms) or math.min(MOVE_INTERVAL_MS, TASK_COMBAT_KITE_SWITCH_MS)
        )
    elseif tostring(type(target) == "table" and target.source or ""):find("^treasure_") == 1 then
        move_interval_ms = math.max(120, tonumber(target.move_interval_ms) or MOVE_INTERVAL_MS)
    elseif type(target) == "table" and target.source == "route_point_board" then
        move_interval_ms = math.max(120, tonumber(target.move_interval_ms) or 250)
    end

    state.next_move_at = current_time + move_interval_ms
    state.last_move_attempt_at = current_time

    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.move_call) ~= "function" then
        log_throttled(ctx, "move_api_missing", "warn", LOG_THROTTLE_MS,
            "[Leveling] nav.move_call is unavailable.")
        return false
    end

    local move_ok, move_err = nav_mod.move_call(target.x, target.y, {
        move_call_mouse_sync = {
            enabled = false
        }
    })
    if not move_ok then
        state.last_move_failure_at = current_time
        state.move_failure_streak = (tonumber(state.move_failure_streak) or 0) + 1
        log_throttled(ctx, "move_call_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] MoveTo failed: " .. tostring(move_err))
        return false, move_err
    end

    state.last_move_call_at = current_time
    state.last_move_failure_at = 0
    state.move_failure_streak = 0
    state.move_guard_until = math.max(
        tonumber(state.move_guard_until) or 0,
        current_time + MOVE_COMBAT_GUARD_MS
    )
    log_throttled(ctx, "move_call_issued", "info", 1500, string.format(
        "[Leveling] MoveTo issued | target=%.2f, %.2f source=%s path_index=%d interval_ms=%d via=direct",
        tonumber(target and target.x) or 0,
        tonumber(target and target.y) or 0,
        tostring(target and target.source or ""),
        tonumber(target and target.path_index) or 0,
        tonumber(move_interval_ms) or 0
    ))
    return true
end

function M.publish_nav_worker_task_path_route(ctx, current_time, target, move_interval_ms)
    local target_source = tostring(type(target) == "table" and target.source or "")
    if M.TASK_PATH_WORKER_ROUTE_MODE ~= true
        or type(target) ~= "table"
        or (target_source ~= "task_path" and target_source ~= "treasure_path")
    then
        return false, "path route worker mode is disabled for source=" .. tostring(target_source)
    end

    local path = state.task_path
    if type(path) ~= "table" or #path <= 0 then
        return false, tostring(target_source) .. " path is unavailable."
    end

    local path_count = #path
    local path_signature = build_path_signature(path)
    local route_identity = tostring(target_source) .. "|" .. tostring(path_signature)
    local requested_direction = tostring(target.path_direction or "")
    local direction = (requested_direction == "reverse" or requested_direction == "fixed_reverse") and -1
        or tonumber(type(state.task_path_route) == "table" and state.task_path_route.direction)
        or 1
    if direction ~= -1 then
        direction = 1
    end
    local arrive_distance = math.max(80, tonumber(target.route_arrive_tolerance) or TASK_PATH_POINT_ARRIVE_TOLERANCE)
    local stuck_skip_ms = math.max(3000, tonumber(target.route_stuck_skip_ms) or TASK_PATH_STUCK_SKIP_MS)
    local progress_reset_distance = math.max(
        40,
        tonumber(target.route_progress_reset_distance) or TASK_PATH_PROGRESS_RESET_DISTANCE
    )
    local worker_target_source = target_source == "treasure_path" and "treasure_path_route" or "task_path_route"

    local nearest_index = tonumber(target.nearest_index)
        or tonumber(type(state.task_path_route) == "table" and state.task_path_route.nearest_index)
        or tonumber(target.route_index)
        or tonumber(target.path_index)
        or 1
    nearest_index = math.max(1, math.min(path_count, math.floor(nearest_index + 0.5)))

    local max_points = math.min(path_count, M.TASK_PATH_WORKER_MAX_POINTS)
    local remaining_count = direction == -1 and nearest_index or (path_count - nearest_index + 1)
    local window_start = tonumber(state.nav_worker_path_route_window_start) or 0
    local window_end = tonumber(state.nav_worker_path_route_window_end) or 0
    local window_direction = tonumber(state.nav_worker_path_route_direction) or direction
    local window_path_signature = tostring(state.nav_worker_path_route_path_signature or "")
    local margin = math.max(6, math.floor(max_points * 0.25))
    local full_route_available = remaining_count <= max_points

    local keep_window = window_start >= 1
        and window_end >= window_start
        and nearest_index >= window_start
        and nearest_index <= window_end
        and window_direction == direction
        and window_path_signature == route_identity
    if keep_window then
        if full_route_available then
            keep_window = direction == 1 and window_end >= path_count or window_start <= 1
        elseif direction == 1 then
            keep_window = window_end >= path_count or (window_end - nearest_index) >= margin
        else
            keep_window = window_start <= 1 or (nearest_index - window_start) >= margin
        end
    end

    local worker_status = tostring(nav_worker_get(ctx, "worker_status") or "")
    local worker_route_version = tonumber(nav_worker_get(ctx, "route_version")) or 0
    local route_count = math.max(0, window_end - window_start + 1)
    if keep_window
        and worker_route_version > 0
        and not (
            worker_status == "path_done"
            and (tonumber(target.current_distance) or 0) > arrive_distance
        )
    then
        local window_first = path[window_start]
        local window_last = path[window_end]
        log_throttled(ctx, "nav_worker_path_route_keep", "info", 1500, string.format(
            "[Leveling] nav worker path route keep | source=%s window=%d-%d/%d direction=%s target_route=%d target_path=%d nearest=%d current_distance=%.2f arrive_distance=%.2f target=%.2f, %.2f first=%.2f, %.2f last=%.2f, %.2f worker_status=%s worker_version=%d",
            tostring(target_source),
            window_start,
            window_end,
            path_count,
            direction == -1 and "reverse" or "forward",
            tonumber(target.route_index) or 0,
            tonumber(target.path_index) or 0,
            nearest_index,
            tonumber(target.current_distance) or 0,
            tonumber(arrive_distance) or 0,
            tonumber(target.x) or 0,
            tonumber(target.y) or 0,
            tonumber(window_first and window_first.x) or 0,
            tonumber(window_first and window_first.y) or 0,
            tonumber(window_last and window_last.x) or 0,
            tonumber(window_last and window_last.y) or 0,
            tostring(worker_status or ""),
            tonumber(worker_route_version) or 0
        ))
        nav_worker_set(ctx, "route_count", route_count)
        nav_worker_set(ctx, "route_arrive_distance", arrive_distance)
        nav_worker_set(ctx, "route_direction", direction)
        nav_worker_set(ctx, "route_stuck_skip_ms", stuck_skip_ms)
        nav_worker_set(ctx, "route_progress_reset_distance", progress_reset_distance)
        nav_worker_set(ctx, "target_source", worker_target_source)
        nav_worker_set(ctx, "target_path_index", tonumber(target.path_index) or 0)
        nav_worker_set(ctx, "move_interval_ms", move_interval_ms)
        nav_worker_set(ctx, "mode", "path_route")
        nav_worker_set(ctx, "paused", false)
        nav_worker_set(ctx, "stop", false)
        state.nav_worker_paused = false
        state.next_move_at = current_time + move_interval_ms
        return true
    end

    if not keep_window then
        if direction == -1 then
            window_end = nearest_index
            window_start = full_route_available and 1 or math.max(1, window_end - max_points + 1)
        else
            window_start = nearest_index
            window_end = full_route_available and path_count or math.min(path_count, window_start + max_points - 1)
        end
    end

    route_count = math.max(0, window_end - window_start + 1)
    if route_count <= 0 then
        return false, "task path route window is empty."
    end

    local parts = {
        target_source,
        path_signature,
        tostring(window_start),
        tostring(window_end),
        tostring(direction),
        tostring(math.floor((tonumber(move_interval_ms) or 0) + 0.5)),
        tostring(math.floor(arrive_distance + 0.5)),
        tostring(math.floor(stuck_skip_ms + 0.5)),
        tostring(math.floor(progress_reset_distance + 0.5)),
        full_route_available and "full" or "chunk"
    }
    for source_index = window_start, window_end do
        local point = path[source_index]
        parts[#parts + 1] = string.format(
            "%d:%.1f:%.1f:%.1f",
            tonumber(point and point.index) or source_index,
            tonumber(point and point.x) or 0,
            tonumber(point and point.y) or 0,
            tonumber(point and point.z) or 0
        )
    end
    local signature = table.concat(parts, "|")
    local route_changed = signature ~= tostring(state.nav_worker_path_route_signature or "")
        or worker_route_version <= 0
        or (worker_status == "path_done"
            and (tonumber(target.current_distance) or 0) > arrive_distance)
    if route_changed then
        state.nav_worker_path_route_version = (tonumber(state.nav_worker_path_route_version) or 0) + 1
        state.nav_worker_path_route_signature = signature
        state.nav_worker_path_route_window_start = window_start
        state.nav_worker_path_route_window_end = window_end
        state.nav_worker_path_route_direction = direction
        state.nav_worker_path_route_path_signature = route_identity

        local output_index = 1
        for source_index = window_start, window_end do
            local point = path[source_index]
            nav_worker_set(ctx, "route_point_" .. output_index .. "_x", tonumber(point and point.x) or 0)
            nav_worker_set(ctx, "route_point_" .. output_index .. "_y", tonumber(point and point.y) or 0)
            nav_worker_set(ctx, "route_point_" .. output_index .. "_z", tonumber(point and point.z) or 0)
            nav_worker_set(ctx, "route_point_" .. output_index .. "_index", tonumber(point and point.index) or source_index)
            output_index = output_index + 1
        end
        local window_first = path[window_start]
        local window_last = path[window_end]
        logger(ctx).info(string.format(
            "[Leveling] nav worker path route armed | source=%s range=%d-%d/%d points=%d mode=%s direction=%s interval_ms=%d arrive_distance=%.2f stuck_skip_ms=%d version=%d target_route=%d target_path=%d nearest=%d current_distance=%.2f target=%.2f, %.2f first=%.2f, %.2f last=%.2f, %.2f signature=%s",
            tostring(target_source),
            window_start,
            window_end,
            path_count,
            route_count,
            full_route_available and "full_remaining" or "chunked",
            direction == -1 and "reverse" or "forward",
            tonumber(move_interval_ms) or 0,
            tonumber(arrive_distance) or 0,
            tonumber(stuck_skip_ms) or 0,
            tonumber(state.nav_worker_path_route_version) or 0,
            tonumber(target.route_index) or 0,
            tonumber(target.path_index) or 0,
            nearest_index,
            tonumber(target.current_distance) or 0,
            tonumber(target.x) or 0,
            tonumber(target.y) or 0,
            tonumber(window_first and window_first.x) or 0,
            tonumber(window_first and window_first.y) or 0,
            tonumber(window_last and window_last.x) or 0,
            tonumber(window_last and window_last.y) or 0,
            tostring(path_signature)
        ))
    end

    nav_worker_set(ctx, "route_count", route_count)
    nav_worker_set(ctx, "route_arrive_distance", arrive_distance)
    nav_worker_set(ctx, "route_direction", direction)
    nav_worker_set(ctx, "route_stuck_skip_ms", stuck_skip_ms)
    nav_worker_set(ctx, "route_progress_reset_distance", progress_reset_distance)
    nav_worker_set(ctx, "target_source", worker_target_source)
    nav_worker_set(ctx, "target_path_index", tonumber(target.path_index) or 0)
    nav_worker_set(ctx, "move_interval_ms", move_interval_ms)
    nav_worker_set(ctx, "mode", "path_route")
    nav_worker_set(ctx, "route_version", tonumber(state.nav_worker_path_route_version) or 1)
    nav_worker_set(ctx, "paused", false)
    nav_worker_set(ctx, "stop", false)
    state.nav_worker_paused = false
    state.next_move_at = current_time + move_interval_ms
    return true
end

local function issue_move(ctx, current_time, target)
    if type(target) ~= "table" then
        return false, "move target is unavailable."
    end

    if LEVELING_USE_NAV_WORKER ~= true then
        return issue_move_direct(ctx, current_time, target)
    end

    local worker_ok, worker_err = ensure_nav_worker_running(ctx, current_time)
    if not worker_ok then
        log_throttled(ctx, "nav_worker_fallback", "warn", LOG_THROTTLE_MS,
            "[Leveling] nav worker unavailable, fallback to direct MoveTo: " .. tostring(worker_err))
        return issue_move_direct(ctx, current_time, target)
    end

    local target_source = tostring(type(target) == "table" and target.source or "")
    local move_interval_ms = MOVE_INTERVAL_MS
    if target_source == "task_path" then
        move_interval_ms = math.max(120, tonumber(target.move_interval_ms) or TASK_PATH_REPATH_INTERVAL_MS)
    elseif target_source == "treasure_path" then
        move_interval_ms = math.max(120, tonumber(target.move_interval_ms) or M.TASK_FOLLOW_MOVE_INTERVAL_MS)
    elseif target_source == "task_pos" or target_source == "task_pos_precise" then
        move_interval_ms = math.max(120, tonumber(target.move_interval_ms) or MOVE_INTERVAL_MS)
    elseif target_source == "task_combat_kite" then
        move_interval_ms = math.max(
            120,
            tonumber(target.move_interval_ms) or math.min(MOVE_INTERVAL_MS, TASK_COMBAT_KITE_SWITCH_MS)
        )
    elseif target_source:find("^treasure_") == 1 then
        move_interval_ms = math.max(120, tonumber(target.move_interval_ms) or MOVE_INTERVAL_MS)
    elseif target_source == "route_point_board" then
        move_interval_ms = math.max(120, tonumber(target.move_interval_ms) or 250)
    end

    if target_source == "task_path" or target_source == "treasure_path" then
        local route_ok, route_err = M.publish_nav_worker_task_path_route(ctx, current_time, target, move_interval_ms)
        if route_ok then
            return true
        end
        log_throttled(ctx, "nav_worker_path_route_fallback_" .. target_source, "warn", LOG_THROTTLE_MS,
            "[Leveling] nav worker path route unavailable, fallback to target MoveTo | source="
                .. tostring(target_source)
                .. " err="
                .. tostring(route_err))
    end

    local signature = nav_worker_target_signature(target)
    local target_path_index = tonumber(target.path_index) or 0
    local published_signature = tostring(state.nav_worker_target_signature or "")
    if type(target) == "table"
        and tostring(target.source or "") == "task_path"
        and signature ~= published_signature
    then
        local last_published_at = tonumber(state.nav_worker_target_published_at) or 0
        local last_published_index = tonumber(state.nav_worker_target_path_index) or 0
        local index_delta = math.abs(target_path_index - last_published_index)
        local current_distance = tonumber(target.current_distance) or math.huge
        local should_hold_publish = last_published_at > 0
            and current_time - last_published_at < TASK_PATH_TARGET_STICK_MS
            and index_delta < TASK_PATH_TARGET_FORCE_INDEX_DELTA
            and current_distance > TASK_PATH_POINT_ARRIVE_TOLERANCE
            and (tonumber(state.stall_retry_count) or 0) <= 0
        if should_hold_publish then
            nav_worker_set(ctx, "paused", false)
            nav_worker_set(ctx, "stop", false)
            state.nav_worker_paused = false
            state.next_move_at = current_time + move_interval_ms
            return true
        end
    end

    local target_changed = signature ~= tostring(state.nav_worker_target_signature or "")
    if target_changed then
        state.nav_worker_target_version = (tonumber(state.nav_worker_target_version) or 0) + 1
        state.nav_worker_target_signature = signature
    end

    nav_worker_set(ctx, "target_x", tonumber(target.x) or 0)
    nav_worker_set(ctx, "target_y", tonumber(target.y) or 0)
    nav_worker_set(ctx, "target_source", tostring(target.source or ""))
    nav_worker_set(ctx, "target_path_index", tonumber(target.path_index) or 0)
    nav_worker_set(ctx, "move_interval_ms", move_interval_ms)
    nav_worker_set(ctx, "mode", "target")
    nav_worker_set(ctx, "target_version", tonumber(state.nav_worker_target_version) or 0)
    nav_worker_set(ctx, "paused", false)
    nav_worker_set(ctx, "stop", false)
    state.nav_worker_paused = false
    if target_changed or (tonumber(state.nav_worker_target_published_at) or 0) <= 0 then
        state.nav_worker_target_published_at = current_time
        state.nav_worker_target_path_index = target_path_index
    end
    if target_changed then
        log_throttled(ctx, "nav_worker_target_published", "info", 1200, string.format(
            "[Leveling] nav worker target published | target=%.2f, %.2f source=%s path_index=%d interval_ms=%d",
            tonumber(target.x) or 0,
            tonumber(target.y) or 0,
            tostring(target.source or ""),
            target_path_index,
            tonumber(move_interval_ms) or 0
        ))
    end
    state.next_move_at = current_time + move_interval_ms
    return true
end

local function should_issue_combat_pulse(current_time, target_distance, is_stalled)
    if current_time < (tonumber(state.pause_combat_until) or 0) then
        return false, "combat pause."
    end
    if current_time < (tonumber(state.move_guard_until) or 0) then
        return false, "move guard."
    end
    if current_time < (tonumber(state.next_action_at) or 0) then
        return false, "combat cooldown."
    end

    if is_stalled == true then
        return true, "stalled"
    end

    return false, "navigation priority."
end

local function should_issue_nearby_monster_pulse(current_time)
    if current_time < (tonumber(state.pause_combat_until) or 0) then
        return false, "combat pause."
    end
    if current_time < (tonumber(state.next_action_at) or 0) then
        return false, "combat cooldown."
    end

    return true, "nearby_monster"
end

function M.should_suppress_follow_nearby_monster_pulse(task_cfg, objective_cfg, target, goal_distance, objective_ready_distance)
    if type(target) ~= "table" or tostring(target.source or "") ~= "task_path" then
        return false, nil
    end
    if type(goal_distance) ~= "number" then
        return false, nil
    end

    local objective_mode = type(objective_cfg) == "table" and tostring(objective_cfg.mode or "") or ""
    if objective_mode ~= "boss_kite" then
        return false, nil
    end

    local threshold = tonumber(type(task_cfg) == "table" and task_cfg.approach_suppress_nearby_monster_pulse_goal_distance)
        or tonumber(type(objective_cfg) == "table" and objective_cfg.approach_suppress_nearby_monster_pulse_goal_distance)
    if type(threshold) ~= "number" or threshold <= 0 then
        return false, nil
    end

    local ready_distance = tonumber(objective_ready_distance) or TARGET_REACHED_DISTANCE
    if goal_distance <= ready_distance or goal_distance > threshold then
        return false, nil
    end

    return true
end

local function should_issue_follow_move_pulse(current_time, goal_distance, is_stalled, target)
    if is_stalled == true then
        return false, "stalled priority."
    end
    if type(target) ~= "table" or tostring(target.source or "") ~= "task_path" then
        return false, "target is not task path."
    end
    if type(goal_distance) ~= "number" or goal_distance <= FOLLOW_MOVE_PULSE_MIN_DISTANCE then
        return false, "goal too near."
    end
    if current_time < (tonumber(state.next_follow_move_pulse_at) or 0) then
        return false, "follow move cooldown."
    end

    local allowed, reason = should_issue_combat_pulse(current_time, goal_distance, false)
    if not allowed then
        return false, reason
    end

    return true, "follow_move"
end

local function issue_combat_pulse(ctx, current_time, reason, ignore_move_guard)
    release_async_combat_inputs(ctx, current_time, false)

    if current_time < (tonumber(state.pause_combat_until) or 0) then
        local wait_ms = (tonumber(state.pause_combat_until) or 0) - current_time
        log_throttled(ctx, "combat_pulse_skipped_pause", "info", 1500, string.format(
            "[Leveling] combat pulse skipped | reason=%s gate=pause wait_ms=%d",
            tostring(reason or ""),
            math.max(0, tonumber(wait_ms) or 0)
        ))
        return false, "combat pause."
    end
    if ignore_move_guard ~= true and current_time < (tonumber(state.move_guard_until) or 0) then
        local wait_ms = (tonumber(state.move_guard_until) or 0) - current_time
        log_throttled(ctx, "combat_pulse_skipped_move_guard", "info", 1500, string.format(
            "[Leveling] combat pulse skipped | reason=%s gate=move_guard wait_ms=%d",
            tostring(reason or ""),
            math.max(0, tonumber(wait_ms) or 0)
        ))
        return false, "move guard."
    end
    if current_time < (tonumber(state.next_action_at) or 0) then
        local wait_ms = (tonumber(state.next_action_at) or 0) - current_time
        log_throttled(ctx, "combat_pulse_skipped_cooldown", "info", 1500, string.format(
            "[Leveling] combat pulse skipped | reason=%s gate=cooldown wait_ms=%d",
            tostring(reason or ""),
            math.max(0, tonumber(wait_ms) or 0)
        ))
        return false, "combat cooldown."
    end

    local cooldown_ms = ACTION_INTERVAL_MS
    state.next_action_at = current_time + cooldown_ms

    local keybd_api = select(1, input_apis(ctx))

    local key_ok = true
    local pulse_mode = "driver_click"
    local post_err = nil

    local driver_api = type(ctx) == "table" and ctx.driver or driver
    if type(driver_api) == "table" and type(driver_api.keybd_click) == "function" then
        local ok, result = safe_call(driver_api.keybd_click, VK_W)
        key_ok = ok and result ~= false
        if key_ok then
            pulse_mode = "driver_click"
        end
    elseif type(keybd_api) == "table" and type(keybd_api.get_mode) == "function" then
        local current_key_mode = nil
        local mode_ok, mode_result = safe_call(keybd_api.get_mode)
        if mode_ok and type(mode_result) == "string" and mode_result ~= "" then
            current_key_mode = mode_result
        end
        if current_key_mode ~= "driver" and type(keybd_api.set_mode) == "function" then
            local set_ok, set_result = safe_call(keybd_api.set_mode, "driver")
            if set_ok and set_result ~= false then
                current_key_mode = "driver"
            end
        end
        if type(keybd_api.click) == "function" then
            local ok, result = safe_call(keybd_api.click, VK_W)
            key_ok = ok and result ~= false
            if key_ok then
                pulse_mode = current_key_mode == "driver" and "key_click_driver" or "key_click"
            end
        else
            key_ok = false
        end
    elseif type(driver_api) == "table" and type(driver_api.keybd_down) == "function" then
        local ok, result = safe_call(driver_api.keybd_down, VK_W)
        key_ok = ok and result ~= false
        if key_ok then
            pulse_mode = "driver_down"
            state.combat_key_down = true
            state.combat_key_release_at = current_time + ACTION_KEY_HOLD_MS
        end
    else
        key_ok = false
    end

    if not key_ok then
        local click_ok, click_err = try_background_key_click(ctx, VK_W, "combat pulse")
        post_err = click_err
        pulse_mode = "post_click"
        if click_ok then
            key_ok = true
        else
            log_throttled(ctx, "combat_pulse_direct_fallback", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] combat pulse direct key send failed, background click fallback also failed | reason=%s err=%s",
                tostring(reason or ""),
                tostring(click_err or "")
            ))
        end
    end

    local mouse_ok = true
    local mouse_mode = "mouse_post_click"
    local mouse_err = nil

    mouse_ok, mouse_err, mouse_mode = M.try_press_right_click_nonintrusive(ctx, "combat pulse")
    if not mouse_ok then
        log_throttled(ctx, "combat_pulse_mouse_failed", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] combat pulse right click failed | reason=%s err=%s",
            tostring(reason or ""),
            tostring(mouse_err or "")
        ))
    end

    if not key_ok or not mouse_ok then
        log_throttled(ctx, "combat_pulse_failed", "warn", LOG_THROTTLE_MS, string.format(
            "[Leveling] combat pulse failed | reason=%s key_ok=%s mouse_ok=%s mode=%s mouse_mode=%s post_err=%s mouse_err=%s",
            tostring(reason or ""),
            tostring(key_ok),
            tostring(mouse_ok),
            tostring(pulse_mode or ""),
            tostring(mouse_mode or ""),
            tostring(post_err or "")
            ,
            tostring(mouse_err or "")
        ))
        return false
    end

    if tostring(reason or "") == "follow_move" then
        state.next_follow_move_pulse_at = current_time + FOLLOW_MOVE_PULSE_INTERVAL_MS
    end

    log_throttled(ctx, "combat_pulse", "info", LOG_THROTTLE_MS, string.format(
        "[Leveling] combat pulse issued | reason=%s mode=%s mouse_mode=%s hold_ms=%d cooldown_ms=%d",
        tostring(reason or ""),
        tostring(pulse_mode or ""),
        tostring(mouse_mode or ""),
        ACTION_KEY_HOLD_MS,
        cooldown_ms
    ))
    return true
end

function M.maybe_issue_route_point_action_route_combat(ctx, current_time, player_x, player_y, point_distance, active_key)
    local action_key = tostring(active_key or "")
    local nearby_monsters, monster_err = M.find_task_monsters(ctx, current_time, player_x, player_y)
    if nearby_monsters and tonumber(nearby_monsters.count) and nearby_monsters.count > 0 then
        local nearest_monster = nearby_monsters.nearest or {}
        log_throttled(
            ctx,
            "route_point_action_route_nearby_monster_" .. action_key,
            "info",
            MONSTER_SCAN_LOG_INTERVAL_MS,
            string.format(
                "[Leveling] recorded route nearby monsters detected | key=%s count=%d total=%d nearest=%s player_distance=%.2f",
                action_key,
                tonumber(nearby_monsters.count) or 0,
                tonumber(nearby_monsters.total_count) or tonumber(nearby_monsters.count) or 0,
                tostring(nearest_monster.label or ""),
                tonumber(nearest_monster.distance) or 0
            )
        )

        local should_pulse, pulse_reason = should_issue_nearby_monster_pulse(current_time)
        if should_pulse then
            local pulse_ok, pulse_err = issue_combat_pulse(ctx, current_time, "nearby_monster", true)
            if pulse_ok then
                logger(ctx).info(string.format(
                    "[Leveling] recorded route nearby monster pulse issued | key=%s",
                    action_key
                ))
                return true
            end
            log_throttled(
                ctx,
                "route_point_action_route_nearby_monster_blocked_" .. action_key,
                "info",
                MONSTER_SCAN_LOG_INTERVAL_MS,
                string.format(
                    "[Leveling] recorded route nearby monster pulse blocked | key=%s nearest_distance=%.2f count=%d reason=%s",
                    action_key,
                    tonumber(nearest_monster.distance) or 0,
                    tonumber(nearby_monsters.count) or 0,
                    tostring(pulse_err or "")
                )
            )
        else
            log_throttled(
                ctx,
                "route_point_action_route_nearby_monster_deferred_" .. action_key,
                "info",
                MONSTER_SCAN_LOG_INTERVAL_MS,
                string.format(
                    "[Leveling] recorded route nearby monster pulse deferred | key=%s nearest_distance=%.2f count=%d reason=%s",
                    action_key,
                    tonumber(nearest_monster.distance) or 0,
                    tonumber(nearby_monsters.count) or 0,
                    tostring(pulse_reason or "")
                )
            )
        end
    elseif monster_err then
        log_throttled(
            ctx,
            "route_point_action_route_monster_scan_miss_" .. action_key,
            "info",
            MONSTER_SCAN_LOG_INTERVAL_MS,
            "[Leveling] recorded route monster scan found no nearby target: " .. tostring(monster_err)
        )
    end

    local should_pulse = false
    local pulse_reason = nil
    if type(point_distance) == "number"
        and point_distance > FOLLOW_MOVE_PULSE_MIN_DISTANCE
        and current_time >= (tonumber(state.next_follow_move_pulse_at) or 0)
    then
        local follow_allowed, follow_reason = should_issue_combat_pulse(current_time, point_distance, false)
        if follow_allowed then
            should_pulse = true
            pulse_reason = "follow_move"
        else
            pulse_reason = follow_reason
        end
    end

    if not should_pulse then
        should_pulse, pulse_reason = should_issue_combat_pulse(current_time, point_distance, false)
    end

    if should_pulse then
        local pulse_ok, pulse_err = issue_combat_pulse(ctx, current_time, pulse_reason)
        if pulse_ok then
            logger(ctx).info(string.format(
                "[Leveling] recorded route combat pulse issued | key=%s reason=%s distance=%.2f",
                action_key,
                tostring(pulse_reason or ""),
                tonumber(point_distance) or 0
            ))
            return true
        end
        log_throttled(
            ctx,
            "route_point_action_route_pulse_blocked_" .. action_key,
            "info",
            MONSTER_SCAN_LOG_INTERVAL_MS,
            string.format(
                "[Leveling] recorded route combat pulse blocked | key=%s reason=%s distance=%.2f err=%s",
                action_key,
                tostring(pulse_reason or ""),
                tonumber(point_distance) or 0,
                tostring(pulse_err or "")
            )
        )
        return false
    end

    log_throttled(
        ctx,
        "route_point_action_route_pulse_deferred_" .. action_key,
        "info",
        MONSTER_SCAN_LOG_INTERVAL_MS,
        string.format(
            "[Leveling] recorded route combat pulse deferred | key=%s distance=%.2f reason=%s",
            action_key,
            tonumber(point_distance) or 0,
            tostring(pulse_reason or "")
        )
    )
    return false
end

local function maybe_consume_potion(ctx, current_time, hp, max_hp, hp_ratio, opts)
    opts = opts or {}

    if opts.force_refresh == true or hp == nil or max_hp == nil or hp_ratio == nil then
        _, hp, max_hp, hp_ratio = refresh_player_status(ctx, current_time, opts.force_refresh == true)
    end

    local hotkey_vk = tonumber(opts.vk) or VK_Q
    local hotkey_name = tostring(opts.hotkey_name or (hotkey_vk == 0x45 and "E" or "Q"))
    local threshold_ratio = tonumber(opts.threshold_ratio)
        or (hotkey_vk == 0x45 and 0.45 or POTION_THRESHOLD_RATIO)
    local last_used_field = tostring(opts.last_used_field or (hotkey_vk == 0x45 and "last_potion_e_at" or "last_potion_q_at"))

    local potion_cooldown_ms = POTION_COOLDOWN_MS
    if type(hp_ratio) == "number" and hp_ratio <= 0.40 then
        potion_cooldown_ms = 900
    end
    if current_time - (tonumber(state[last_used_field]) or 0) < potion_cooldown_ms then
        return false
    end

    if type(hp_ratio) ~= "number" or hp_ratio > threshold_ratio then
        return false
    end

    local ok, err, mode = nil, nil, nil
    if opts.nonintrusive == true then
        ok, err, mode = M.try_press_potion_hotkey(ctx, hotkey_vk, "leveling potion " .. hotkey_name)
    else
        ok, err = press_keyboard_hotkey(ctx, current_time, hotkey_vk, "leveling potion " .. hotkey_name)
        mode = ok and "default" or nil
    end
    if not ok then
        log_throttled(ctx, "potion_failed_" .. hotkey_name, "warn", LOG_THROTTLE_MS,
            string.format("[Leveling] low hp potion %s failed: %s", hotkey_name, tostring(err)))
        return false, err
    end

    state.last_potion_at = current_time
    state[last_used_field] = current_time
    logger(ctx).info(string.format(
        "[Leveling] potion used | key=%s hp=%s max_hp=%s ratio=%.2f threshold=%.2f mode=%s source=%s cooldown_ms=%d",
        hotkey_name,
        hp ~= nil and string.format("%.2f", tonumber(hp) or 0) or "nil",
        max_hp ~= nil and string.format("%.2f", tonumber(max_hp) or 0) or "nil",
        tonumber(hp_ratio) or 0,
        tonumber(threshold_ratio) or 0,
        tostring(mode or ""),
        opts.watch == true and "watch" or "default",
        tonumber(potion_cooldown_ms) or 0
    ))
    return true
end

M.maybe_handle_potion_watch = function(ctx, current_time, hp, max_hp, hp_ratio)
    current_time = tonumber(current_time) or now_ms(ctx)
    if current_time < (tonumber(state.next_potion_watch_at) or 0) then
        return false
    end

    state.next_potion_watch_at = current_time + 120

    local last_info_at = tonumber(state.last_player_info_at) or 0
    local info_age_ms = last_info_at > 0 and math.max(0, current_time - last_info_at) or math.huge
    local force_refresh = hp == nil
        or max_hp == nil
        or hp_ratio == nil
        or info_age_ms >= 160
        or (type(hp_ratio) == "number" and hp_ratio <= 0.72)

    local common_opts = {
        nonintrusive = true,
        force_refresh = force_refresh,
        watch = true
    }

    local used_q = maybe_consume_potion(ctx, current_time, hp, max_hp, hp_ratio, {
        nonintrusive = common_opts.nonintrusive,
        force_refresh = common_opts.force_refresh,
        watch = common_opts.watch,
        vk = VK_Q,
        hotkey_name = "Q",
        threshold_ratio = POTION_THRESHOLD_RATIO,
        last_used_field = "last_potion_q_at"
    })
    if used_q then
        return true
    end

    return maybe_consume_potion(ctx, current_time, hp, max_hp, hp_ratio, {
        nonintrusive = common_opts.nonintrusive,
        force_refresh = common_opts.force_refresh,
        watch = common_opts.watch,
        vk = 0x45,
        hotkey_name = "E",
        threshold_ratio = 0.45,
        last_used_field = "last_potion_e_at"
    })
end

local function reset_revive_state()
    state.revive_started_at = 0
    state.revive_clicked_at = 0
    state.revive_click_count = 0
    state.revive_resume_ready_at = 0
    state.next_revive_click_at = 0
    state.post_revive_boss_engage_until = 0
    state.startup_state_resolve_until = 0
end

M.clear_revive_reentry_state = function()
    state.revive_reentry_pending = false
    state.revive_reentry_map_name = nil
    state.revive_reentry_cfg = nil
    state.revive_reentry_source = nil
    state.revive_reentry_objective_key = nil
    state.revive_reentry_deadline_at = 0
end

clear_task_combat_state = function()
    M.pause_task_combat_kite_route_worker(state.log_ctx)
    state.task_combat_started_at = 0
    state.task_combat_last_seen_at = 0
    state.task_combat_last_count = 0
    state.task_combat_locked_task_name = nil
    state.task_combat_locked_task_detail = nil
    state.task_combat_locked_objective_key = nil
    state.task_combat_locked_reentry_cfg = nil
    state.task_combat_locked_reentry_source = nil
    state.task_combat_anchor_x = nil
    state.task_combat_anchor_y = nil
    state.task_combat_anchor_z = nil
    state.task_combat_kite_phase = 0
    state.task_combat_next_kite_switch_at = 0
    state.task_combat_kite_points = nil
    state.task_combat_kite_index = 0
    state.task_combat_kite_template_points = nil
    state.task_combat_kite_switch_ms = nil
    state.task_combat_kite_seamless = false
    state.task_combat_kite_async_worker = false
    state.task_combat_kite_route_worker_signature = nil
    state.task_combat_kite_route_worker_version = 0
    state.task_combat_kite_route_worker_active = false
    state.task_combat_kite_arrive_distance = nil
    state.task_combat_kite_move_interval_ms = nil
    state.task_combat_kite_force_move = false
    state.task_combat_kite_anchor_route_x = nil
    state.task_combat_kite_anchor_route_y = nil
    state.task_combat_kite_anchor_route_z = nil
    state.task_combat_kite_radius = nil
    state.task_combat_force_kite = false
    state.boss_soft_task_change_candidate = nil
    state.boss_soft_task_change_first_at = 0
    state.boss_soft_task_change_seen_count = 0
    state.boss_soft_task_change_confirmed_candidate = nil
    state.boss_soft_task_change_confirmed_at = 0
    M.clear_post_combat_loot_state()
end


clear_runtime_objective_caches = function()
    state.cached_nearest_npc = nil
    state.cached_npc_error = nil
    state.cached_task_monsters = nil
    state.cached_task_monster_error = nil
    state.cached_task_objective_button = nil
    state.cached_task_objective_button_error = nil
    state.cached_task_objective_button_key = nil
    state.next_task_objective_button_scan_at = 0
    state.cached_interaction_prompt_target = nil
    state.cached_interaction_prompt_error = nil
    state.next_interaction_prompt_scan_at = 0
    state.cached_exit_portal_target = nil
    state.cached_exit_portal_error = nil
    state.next_exit_portal_scan_at = 0
end

function M.maybe_click_guide_skip(ctx, current_time)
    current_time = tonumber(current_time) or now_ms(ctx)
    if current_time < (tonumber(state.next_guide_skip_scan_at) or 0) then
        return false
    end
    local scan_interval_ms = 650
    if state.stage == "follow_task" then
        local last_move_call_at = tonumber(state.last_move_call_at) or 0
        if last_move_call_at > 0 and current_time - last_move_call_at < 1400 then
            scan_interval_ms = 1800
        else
            scan_interval_ms = 1200
        end
    end
    state.next_guide_skip_scan_at = current_time + scan_interval_ms

    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table"
        or type(nav_mod.find_button_near_point) ~= "function"
        or type(nav_mod.control_click) ~= "function"
    then
        return false
    end

    local step = M.GUIDE_SKIP_STEP or {}
    local target, fetch_err = nav_mod.find_button_near_point(
        tonumber(step.hint_client_x) or 0,
        tonumber(step.hint_client_y) or 0,
        {
            include_patterns = step.include_patterns,
            max_distance = tonumber(step.hint_max_distance) or 80
        }
    )
    if type(target) ~= "table" then
        if fetch_err then
            log_throttled(ctx, "guide_skip_missing", "info", LOG_THROTTLE_MS,
                "[Leveling] guide skip button not visible: " .. tostring(fetch_err))
        end
        return false
    end
    if current_time < (tonumber(state.next_guide_skip_click_at) or 0) then
        state.next_guide_skip_scan_at = math.max(
            tonumber(state.next_guide_skip_scan_at) or current_time,
            tonumber(state.next_guide_skip_click_at) or current_time
        )
        return false
    end

    release_async_combat_inputs(ctx, current_time, true)
    local clicked, click_err = nav_mod.control_click(target.addr)
    if not clicked then
        log_throttled(ctx, "guide_skip_click_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] guide skip button click failed: " .. tostring(click_err))
        state.next_guide_skip_click_at = current_time + 600
        return false
    end

    state.next_guide_skip_click_at = current_time + 1200
    state.pause_combat_until = math.max(tonumber(state.pause_combat_until) or 0, current_time + POST_UI_PAUSE_MS)
    state.next_move_at = math.max(tonumber(state.next_move_at) or 0, current_time + 120)
    clear_runtime_objective_caches()
    logger(ctx).info(string.format(
        "[Leveling] guide skip button clicked | label=%s addr=%s pos=(%s,%s)",
        tostring(step.label or ""),
        tostring(target.addr or ""),
        tostring(target.x or ""),
        tostring(target.y or "")
    ))
    return true
end

local function clear_pending_interaction(keep_npc_dialogue_retry_state)
    state.pending_interaction_origin = nil
    state.pending_interaction_label = nil
    state.pending_interaction_refresh_on_timeout = false
    M.clear_task_dialogue_flow_state()
    if keep_npc_dialogue_retry_state ~= true then
        M.clear_npc_dialogue_combat_retry_state()
    end
end

function M.dialogue_flow_matches_origin(flow, interaction_origin)
    local origin = trim(interaction_origin)
    if origin == "" then
        return false
    end

    local allowed_origins = nil
    if type(flow) == "table" and type(flow.origins) == "table" and #flow.origins > 0 then
        allowed_origins = flow.origins
    elseif type(flow) == "table" and trim(flow.origin or "") ~= "" then
        allowed_origins = { flow.origin }
    end

    if type(allowed_origins) ~= "table" then
        return true
    end

    for _, item in ipairs(allowed_origins) do
        if trim(item or "") == origin then
            return true
        end
    end
    return false
end

local fetch_locator_button_target
local click_locator_button_target

function M.maybe_handle_task_dialogue_flow(ctx, current_time)
    current_time = tonumber(current_time) or now_ms(ctx)

    local interaction_origin = trim(state.pending_interaction_origin)
    if interaction_origin == "" then
        M.clear_task_dialogue_flow_state()
        return false
    end

    local flow, task_name = M.current_task_dialogue_flow_config()
    if type(flow) ~= "table" then
        M.clear_task_dialogue_flow_state()
        return false
    end
    if M.dialogue_flow_matches_origin(flow, interaction_origin) ~= true then
        M.clear_task_dialogue_flow_state()
        return false
    end

    local flow_key = tostring(flow.key or task_name or state.current_task_name or "task_dialogue_flow")
    local steps = flow.steps
    if type(steps) ~= "table" or #steps <= 0 then
        M.clear_task_dialogue_flow_state()
        return false
    end

    if tostring(state.task_dialogue_flow_key or "") ~= flow_key
        or tostring(state.task_dialogue_flow_last_origin or "") ~= interaction_origin
    then
        M.clear_task_dialogue_flow_state()
        state.task_dialogue_flow_key = flow_key
        state.task_dialogue_flow_step_index = 1
        state.task_dialogue_flow_started_at = current_time
        state.task_dialogue_flow_deadline_at = current_time + math.max(3000, tonumber(flow.timeout_ms) or 8000)
        state.task_dialogue_flow_next_retry_at = current_time
        state.task_dialogue_flow_last_origin = interaction_origin
        logger(ctx).info(string.format(
            "[Leveling] task dialogue flow armed | task=%s key=%s origin=%s steps=%d timeout=%dms pending_label=%s",
            tostring(task_name or state.current_task_name or ""),
            flow_key,
            interaction_origin,
            #steps,
            math.max(3000, tonumber(flow.timeout_ms) or 8000),
            tostring(state.pending_interaction_label or "")
        ))
    end

    local deadline_at = tonumber(state.task_dialogue_flow_deadline_at) or 0
    if deadline_at > 0 and current_time > deadline_at then
        log_throttled(ctx, "task_dialogue_flow_timeout_" .. flow_key, "warn", LOG_THROTTLE_MS, string.format(
            "[Leveling] task dialogue flow timeout, fallback to normal jump | task=%s key=%s origin=%s step=%d/%d pending_label=%s",
            tostring(task_name or state.current_task_name or ""),
            flow_key,
            interaction_origin,
            math.max(1, tonumber(state.task_dialogue_flow_step_index) or 1),
            #steps,
            tostring(state.pending_interaction_label or "")
        ))
        M.clear_task_dialogue_flow_state()
        return false
    end

    state.stage = "task_dialogue_flow"
    hold_navigation(ctx, current_time, "task_dialogue_flow")

    local step_index = math.max(1, tonumber(state.task_dialogue_flow_step_index) or 1)
    local next_retry_at = tonumber(state.task_dialogue_flow_next_retry_at) or 0
    local step = steps[step_index]
    if type(step) ~= "table" then
        if current_time < next_retry_at then
            log_throttled(ctx, "task_dialogue_flow_settle_" .. flow_key, "info", 1200, string.format(
                "[Leveling] task dialogue flow settling | task=%s key=%s origin=%s wait_left=%dms",
                tostring(task_name or state.current_task_name or ""),
                flow_key,
                interaction_origin,
                math.max(0, next_retry_at - current_time)
            ))
            return true
        end

        logger(ctx).info(string.format(
            "[Leveling] task dialogue flow completed | task=%s key=%s origin=%s steps=%d",
            tostring(task_name or state.current_task_name or ""),
            flow_key,
            interaction_origin,
            #steps
        ))
        M.clear_task_dialogue_flow_state()
        return false
    end

    if current_time < next_retry_at then
        log_throttled(ctx, "task_dialogue_flow_wait_" .. flow_key .. "_" .. tostring(step.key or step_index), "info", 1000,
            string.format(
                "[Leveling] task dialogue flow waiting retry | task=%s key=%s step=%d/%d step_key=%s retry_in=%dms",
                tostring(task_name or state.current_task_name or ""),
                flow_key,
                step_index,
                #steps,
                tostring(step.key or ""),
                math.max(0, next_retry_at - current_time)
            ))
        return true
    end

    local target, fetch_err = fetch_locator_button_target(ctx, step)
    if type(target) ~= "table" then
        state.task_dialogue_flow_next_retry_at = current_time + math.max(300, tonumber(step.retry_ms) or 600)
        log_throttled(ctx, "task_dialogue_flow_missing_" .. flow_key .. "_" .. tostring(step.key or step_index), "info", 1200,
            string.format(
                "[Leveling] task dialogue flow step target not visible yet | task=%s key=%s step=%d/%d step_key=%s label=%s origin=%s err=%s deadline_in=%dms",
                tostring(task_name or state.current_task_name or ""),
                flow_key,
                step_index,
                #steps,
                tostring(step.key or ""),
                tostring(step.label or ""),
                interaction_origin,
                tostring(fetch_err or ""),
                math.max(0, deadline_at - current_time)
            ))
        return true
    end

    release_async_combat_inputs(ctx, current_time, true)
    local clicked, click_err, retryable = click_locator_button_target(ctx, step, target)
    if not clicked then
        state.task_dialogue_flow_next_retry_at = current_time + math.max(300, tonumber(step.retry_ms) or 600)
        log_throttled(
            ctx,
            (retryable and "task_dialogue_flow_retry_" or "task_dialogue_flow_click_failed_")
                .. flow_key .. "_" .. tostring(step.key or step_index),
            retryable and "info" or "warn",
            LOG_THROTTLE_MS,
            string.format(
                "[Leveling] task dialogue flow step click failed | task=%s key=%s step=%d/%d step_key=%s label=%s origin=%s retryable=%s err=%s",
                tostring(task_name or state.current_task_name or ""),
                flow_key,
                step_index,
                #steps,
                tostring(step.key or ""),
                tostring(step.label or ""),
                interaction_origin,
                retryable and "true" or "false",
                tostring(click_err or "")
            )
        )
        return true
    end

    local after_click_time = now_ms(ctx)
    local settle_ms = math.max(150, tonumber(step.settle_ms) or tonumber(flow.settle_ms) or 800)
    state.task_dialogue_flow_step_index = step_index + 1
    state.task_dialogue_flow_next_retry_at = after_click_time + settle_ms
    state.pause_combat_until = math.max(tonumber(state.pause_combat_until) or 0, after_click_time + POST_UI_PAUSE_MS)
    logger(ctx).info(string.format(
        "[Leveling] task dialogue flow step clicked | task=%s key=%s step=%d/%d step_key=%s label=%s origin=%s addr=%s pos=(%s,%s) settle_ms=%d",
        tostring(task_name or state.current_task_name or ""),
        flow_key,
        step_index,
        #steps,
        tostring(step.key or ""),
        tostring(step.label or ""),
        interaction_origin,
        tostring(target.addr or ""),
        tostring(target.x or ""),
        tostring(target.y or ""),
        settle_ms
    ))
    return true
end

function M.arm_post_dialogue_flow(ctx, current_time, flow, task_name, interaction_origin)
    if type(flow) ~= "table" or type(flow.steps) ~= "table" or #flow.steps <= 0 then
        M.clear_post_dialogue_flow_state()
        return false
    end

    local flow_key = tostring(flow.key or task_name or state.current_task_name or "post_dialogue_flow")
    state.post_dialogue_flow_key = flow_key
    state.post_dialogue_flow_steps = flow.steps
    state.post_dialogue_flow_step_index = 1
    state.post_dialogue_flow_started_at = current_time
    state.post_dialogue_flow_deadline_at = current_time + math.max(3000, tonumber(flow.timeout_ms) or 8000)
    state.post_dialogue_flow_next_retry_at = current_time + math.max(0, tonumber(flow.initial_delay_ms) or 500)
    state.post_dialogue_flow_task_name = tostring(task_name or state.current_task_name or "")
    state.post_dialogue_flow_origin = tostring(interaction_origin or "")
    state.post_dialogue_flow_skip_dialogue_jump = flow.skip_dialogue_jump == true
        or flow.arm_after_objective_button == true
    logger(ctx).info(string.format(
        "[Leveling] post dialogue flow armed | task=%s key=%s origin=%s steps=%d initial_delay=%dms timeout=%dms skip_jump=%s",
        tostring(state.post_dialogue_flow_task_name or ""),
        flow_key,
        tostring(state.post_dialogue_flow_origin or ""),
        #flow.steps,
        math.max(0, tonumber(flow.initial_delay_ms) or 500),
        math.max(3000, tonumber(flow.timeout_ms) or 8000),
        state.post_dialogue_flow_skip_dialogue_jump == true and "true" or "false"
    ))
    return true
end

function M.maybe_handle_post_dialogue_flow(ctx, current_time)
    current_time = tonumber(current_time) or now_ms(ctx)
    local flow_key = tostring(state.post_dialogue_flow_key or "")
    if flow_key == "" then
        return false
    end

    local steps = state.post_dialogue_flow_steps
    if type(steps) ~= "table" or #steps <= 0 then
        M.clear_post_dialogue_flow_state()
        return false
    end

    local deadline_at = tonumber(state.post_dialogue_flow_deadline_at) or 0
    if deadline_at > 0 and current_time > deadline_at then
        log_throttled(ctx, "post_dialogue_flow_timeout_" .. flow_key, "warn", LOG_THROTTLE_MS, string.format(
            "[Leveling] post dialogue flow timeout, fallback to task refresh | task=%s key=%s step=%d/%d",
            tostring(state.post_dialogue_flow_task_name or state.current_task_name or ""),
            flow_key,
            math.max(1, tonumber(state.post_dialogue_flow_step_index) or 1),
            #steps
        ))
        M.clear_post_dialogue_flow_state()
        return false
    end

    state.stage = "post_dialogue_flow"
    hold_navigation(ctx, current_time, "post_dialogue_flow")

    local next_retry_at = tonumber(state.post_dialogue_flow_next_retry_at) or 0
    if current_time < next_retry_at then
        log_throttled(ctx, "post_dialogue_flow_wait_" .. flow_key, "info", 1000, string.format(
            "[Leveling] post dialogue flow waiting | task=%s key=%s wait_left=%dms",
            tostring(state.post_dialogue_flow_task_name or state.current_task_name or ""),
            flow_key,
            math.max(0, next_retry_at - current_time)
        ))
        return true
    end

    local step_index = math.max(1, tonumber(state.post_dialogue_flow_step_index) or 1)
    local step = steps[step_index]
    if type(step) ~= "table" then
        local should_open_jump_window = state.post_dialogue_flow_skip_dialogue_jump == true
        logger(ctx).info(string.format(
            "[Leveling] post dialogue flow completed | task=%s key=%s steps=%d",
            tostring(state.post_dialogue_flow_task_name or state.current_task_name or ""),
            flow_key,
            #steps
        ))
        M.clear_post_dialogue_flow_state()
        if should_open_jump_window then
            state.task_update_wait_until = math.max(
                tonumber(state.task_update_wait_until) or 0,
                current_time + math.max(TASK_BUTTON_SETTLE_MS, 900)
            )
            state.next_dialogue_jump_scan_at = 0
            state.next_dialogue_jump_click_at = 0
            logger(ctx).info(string.format(
                "[Leveling] post dialogue pre-jump flow completed; dialogue jump scan window opened | key=%s wait=%dms",
                flow_key,
                math.max(0, (tonumber(state.task_update_wait_until) or current_time) - current_time)
            ))
        end
        return true
    end

    local target, fetch_err = fetch_locator_button_target(ctx, step)
    if type(target) ~= "table" then
        state.post_dialogue_flow_next_retry_at = current_time + math.max(300, tonumber(step.retry_ms) or 600)
        log_throttled(ctx, "post_dialogue_flow_missing_" .. flow_key .. "_" .. tostring(step.key or step_index), "info", 1200,
            string.format(
                "[Leveling] post dialogue flow step target not ready | task=%s key=%s step=%d/%d step_key=%s label=%s err=%s deadline_in=%dms",
                tostring(state.post_dialogue_flow_task_name or state.current_task_name or ""),
                flow_key,
                step_index,
                #steps,
                tostring(step.key or ""),
                tostring(step.label or ""),
                tostring(fetch_err or ""),
                math.max(0, deadline_at - current_time)
            ))
        return true
    end

    release_async_combat_inputs(ctx, current_time, true)
    local clicked, click_err, retryable = click_locator_button_target(ctx, step, target)
    if not clicked then
        state.post_dialogue_flow_next_retry_at = current_time + math.max(300, tonumber(step.retry_ms) or 600)
        log_throttled(
            ctx,
            (retryable and "post_dialogue_flow_retry_" or "post_dialogue_flow_click_failed_")
                .. flow_key .. "_" .. tostring(step.key or step_index),
            retryable and "info" or "warn",
            LOG_THROTTLE_MS,
            string.format(
                "[Leveling] post dialogue flow step click failed | task=%s key=%s step=%d/%d step_key=%s label=%s retryable=%s err=%s",
                tostring(state.post_dialogue_flow_task_name or state.current_task_name or ""),
                flow_key,
                step_index,
                #steps,
                tostring(step.key or ""),
                tostring(step.label or ""),
                retryable and "true" or "false",
                tostring(click_err or "")
            )
        )
        return true
    end

    local after_click_time = now_ms(ctx)
    local settle_ms = math.max(150, tonumber(step.settle_ms) or 800)
    state.post_dialogue_flow_step_index = step_index + 1
    state.post_dialogue_flow_next_retry_at = after_click_time + settle_ms
    state.task_update_wait_until = math.max(tonumber(state.task_update_wait_until) or 0, after_click_time + settle_ms)
    state.next_task_button_click_at = math.max(tonumber(state.next_task_button_click_at) or 0, after_click_time + settle_ms)
    state.next_task_refresh_at = math.max(tonumber(state.next_task_refresh_at) or 0, after_click_time + settle_ms)
    state.pause_combat_until = math.max(tonumber(state.pause_combat_until) or 0, after_click_time + POST_UI_PAUSE_MS)
    if step.force_task_call_after_transition == true then
        schedule_task_refresh_after_transition(
            ctx,
            after_click_time,
            "post_dialogue_flow_" .. tostring(step.key or step.label or ""),
            settle_ms,
            {
                force_task_call = true,
                task_pos_reject_extra_ms = tonumber(step.task_pos_reject_extra_ms) or 2500
            }
        )
    end
    logger(ctx).info(string.format(
        "[Leveling] post dialogue flow step clicked | task=%s key=%s step=%d/%d step_key=%s label=%s kind=%s pos=(%s,%s) settle_ms=%d",
        tostring(state.post_dialogue_flow_task_name or state.current_task_name or ""),
        flow_key,
        step_index,
        #steps,
        tostring(step.key or ""),
        tostring(step.label or ""),
        tostring(target.kind or ""),
        tostring(target.x or ""),
        tostring(target.y or ""),
        settle_ms
    ))
    return true
end

function M.maybe_click_dialogue_jump_button(ctx, current_time)
    current_time = tonumber(current_time) or now_ms(ctx)
    if tostring(state.post_dialogue_flow_key or "") ~= ""
        and state.post_dialogue_flow_skip_dialogue_jump == true
    then
        log_throttled(ctx, "dialogue_jump_skipped_by_post_flow_" .. tostring(state.post_dialogue_flow_key or ""), "info", LOG_THROTTLE_MS,
            "[Leveling] dialogue jump skipped; post dialogue flow will handle fixed mouse click directly.")
        return false
    end

    local interaction_pending = tostring(state.pending_interaction_origin or "") ~= ""
    local in_dialogue_window = interaction_pending
        or current_time < (tonumber(state.task_update_wait_until) or 0)
        or state.require_task_button_refresh == true
    if not in_dialogue_window then
        return false
    end

    local step = nil
    for _, item in ipairs(M.TASK_OBJECTIVE_BUTTON_STEPS or {}) do
        if tostring(item.key or "") == "jump_btn"
            or tostring(item.distance_button_name or "") == "UIButton Transient.GameEngine.CoreGameInstance.DialogueTalk_C.WidgetTree.JumpBtn"
        then
            step = item
            break
        end
    end
    if type(step) ~= "table" then
        return false
    end

    if current_time < (tonumber(state.next_dialogue_jump_scan_at) or 0) then
        return false
    end
    state.next_dialogue_jump_scan_at = current_time + 250

    local target, fetch_err = M.fetch_hint_button_target(ctx, step)
    if type(target) ~= "table" then
        if fetch_err then
            log_throttled(ctx, "dialogue_jump_missing", "info", LOG_THROTTLE_MS,
                "[Leveling] dialogue jump button not visible: " .. tostring(fetch_err))
        end
        return false
    end

    if current_time < (tonumber(state.next_dialogue_jump_click_at) or 0) then
        return false
    end

    local interaction_origin = tostring(state.pending_interaction_origin or "")
    release_async_combat_inputs(ctx, current_time, true)
    local clicked = false
    local click_err = nil
    local retryable = false
    if type(click_fetched_target) == "function" then
        local ok, result_clicked, result_err, result_retryable = safe_call(click_fetched_target, step, target)
        if ok then
            clicked = result_clicked == true
            click_err = result_err
            retryable = result_retryable == true
        else
            clicked = false
            click_err = result_clicked
        end
    else
        local nav_mod = nav_api(ctx)
        if type(nav_mod) ~= "table" or type(nav_mod.control_click) ~= "function" then
            click_err = "nav.control_click is unavailable."
        elseif type(target) ~= "table" or tonumber(target.addr) == nil then
            click_err = "Invalid dialogue jump target."
        else
            local ok_click, err_or_retry = nav_mod.control_click(target.addr)
            clicked = ok_click == true
            click_err = err_or_retry
        end
    end
    if not clicked then
        log_throttled(
            ctx,
            retryable and "dialogue_jump_click_retry" or "dialogue_jump_click_failed",
            retryable and "info" or "warn",
            LOG_THROTTLE_MS,
            "[Leveling] dialogue jump button click failed: " .. tostring(click_err)
        )
        state.next_dialogue_jump_click_at = current_time + 500
        return false
    end

    state.next_dialogue_jump_click_at = current_time + 900
    state.dialogue_escape_due_at = 0
    state.dialogue_confirm_deadline_at = 0
    state.next_dialogue_probe_at = 0
    state.next_dialogue_jump_scan_at = 0
    state.next_dialogue_jump_click_at = 0
    state.dialogue_ui_confirmed = false
    state.dialogue_ui_match = nil
    local post_flow, post_task_name = M.current_task_post_dialogue_flow_config()
    local armed_post_dialogue_flow = false
    if type(post_flow) == "table" and post_flow.arm_after_objective_button ~= true then
        armed_post_dialogue_flow = M.arm_post_dialogue_flow(ctx, current_time, post_flow, post_task_name, interaction_origin) == true
    end
    clear_pending_interaction()
    state.task_update_wait_until = math.max(
        tonumber(state.task_update_wait_until) or 0,
        current_time + math.max(tonumber(step.settle_ms) or TASK_BUTTON_SETTLE_MS, TASK_BUTTON_SETTLE_MS)
    )
    if armed_post_dialogue_flow then
        state.require_task_button_refresh = false
        state.require_task_button_refresh_reason = nil
        state.next_task_button_click_at = math.max(
            tonumber(state.next_task_button_click_at) or 0,
            tonumber(state.task_update_wait_until) or current_time
        )
        state.next_task_refresh_at = math.max(
            tonumber(state.next_task_refresh_at) or 0,
            tonumber(state.task_update_wait_until) or current_time
        )
        logger(ctx).info(string.format(
            "[Leveling] dialogue jump refresh deferred by post dialogue flow | task=%s key=%s",
            tostring(post_task_name or state.current_task_name or ""),
            tostring(post_flow.key or "")
        ))
    else
        state.require_task_button_refresh = true
        state.next_task_button_click_at = math.max(
            tonumber(state.next_task_button_click_at) or 0,
            tonumber(state.task_update_wait_until) or current_time
        )
        state.next_task_refresh_at = math.max(
            tonumber(state.next_task_refresh_at) or 0,
            tonumber(state.task_update_wait_until) or current_time
        )
    end
    state.pause_combat_until = math.max(
        tonumber(state.pause_combat_until) or 0,
        current_time + POST_UI_PAUSE_MS
    )
    logger(ctx).info(string.format(
        "[Leveling] dialogue jump button clicked | label=%s addr=%s pos=(%s,%s) origin=%s",
        tostring(step.label or ""),
        tostring(target.addr or ""),
        tostring(target.x or ""),
        tostring(target.y or ""),
        interaction_origin
    ))
    return true
end

function M.fetch_hint_button_target(ctx, step)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.find_button_near_point) ~= "function" then
        return nil, "button locator API is unavailable."
    end

    local hint_x = tonumber(step and step.hint_client_x)
    local hint_y = tonumber(step and step.hint_client_y)
    if hint_x == nil or hint_y == nil then
        return nil, "locator hint point is unavailable."
    end

    return nav_mod.find_button_near_point(hint_x, hint_y, {
        include_patterns = type(step) == "table" and step.include_patterns or nil,
        max_distance = tonumber(type(step) == "table" and step.hint_max_distance) or 80
    })
end

function M.resolve_fixed_client_click_target(ctx, step)
    if type(step) ~= "table" or step.fixed_client_click ~= true then
        return nil
    end

    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.window_hwnd) ~= "function" then
        return nil, "nav.window_hwnd is unavailable."
    end

    local hwnd, hwnd_err = nav_mod.window_hwnd()
    if not hwnd then
        return nil, hwnd_err or "game window not found."
    end

    local wnd_api = type(ctx) == "table" and ctx.wnd or wnd
    if type(wnd_api) ~= "table" or type(wnd_api.client_rect) ~= "function" then
        return nil, "wnd.client_rect is unavailable."
    end

    local origin_x, origin_y, client_w, client_h = wnd_api.client_rect(hwnd)
    if type(origin_x) ~= "number"
        or type(origin_y) ~= "number"
        or type(client_w) ~= "number"
        or type(client_h) ~= "number"
        or client_w <= 0
        or client_h <= 0
    then
        return nil, "wnd.client_rect failed."
    end

    local client_x = tonumber(step.fixed_client_x)
    local client_y = tonumber(step.fixed_client_y)
    local ratio_x = tonumber(step.fixed_ratio_x)
    local ratio_y = tonumber(step.fixed_ratio_y)
    if step.fixed_prefer_ratio ~= false and ratio_x ~= nil and ratio_y ~= nil then
        client_x = ratio_x * client_w
        client_y = ratio_y * client_h
    end
    if client_x == nil or client_y == nil then
        return nil, "fixed client point is unavailable."
    end

    if step.allow_outside ~= true
        and (client_x < 0 or client_y < 0 or client_x > client_w or client_y > client_h)
    then
        return nil, string.format(
            "fixed client point outside window. client=(%.2f, %.2f) size=(%.2f, %.2f)",
            tonumber(client_x) or 0,
            tonumber(client_y) or 0,
            tonumber(client_w) or 0,
            tonumber(client_h) or 0
        )
    end

    return {
        kind = "client_point",
        name = "fixed_client_click",
        text = tostring(step.label or ""),
        fullname = tostring(step.key or ""),
        x = client_x,
        y = client_y,
        hwnd = hwnd,
        click_screen_x = math.floor(origin_x + client_x + 0.5),
        click_screen_y = math.floor(origin_y + client_y + 0.5),
        click_button = tostring(step.click_button or "left"),
        click_mode = tostring(step.mouse_mode or step.click_mode or "api"),
        click_delay = tonumber(step.click_delay_ms) or tonumber(step.click_delay) or 50,
        hover_delay_ms = tonumber(step.hover_delay_ms) or 80
    }
end

function fetch_locator_button_target(ctx, step)
    local fixed_target, fixed_err = M.resolve_fixed_client_click_target(ctx, step)
    if fixed_target then
        return fixed_target
    end
    if type(step) == "table" and step.fixed_client_click == true then
        return nil, fixed_err
    end

    if type(fetch_button_for_step) == "function" then
        local ok, target, fetch_err = safe_call(fetch_button_for_step, step)
        if ok and target then
            return target
        end
        if ok
            and type(step) == "table"
            and step.prefer_hint_fallback == true
        then
            local hint_target, hint_err = M.fetch_hint_button_target(ctx, step)
            if hint_target then
                return hint_target
            end
            return nil, hint_err or fetch_err
        end
        if ok and type(step) == "table" and (step.distance_anchor_exact_text == nil or tostring(step.distance_anchor_exact_text) == "") then
            return M.fetch_hint_button_target(ctx, step)
        end
        if ok then
            return nil, fetch_err
        end
        return nil, target
    end

    return M.fetch_hint_button_target(ctx, step)
end

function click_locator_button_target(ctx, step, target)
    if type(target) == "table" and tostring(target.kind or "") == "client_point" then
        logger(ctx).info(string.format(
            "[Leveling] fixed client click dispatch | step_key=%s label=%s client=(%.2f, %.2f) screen=(%s, %s) hwnd=%s mode=%s",
            tostring(type(step) == "table" and step.key or ""),
            tostring(type(step) == "table" and step.label or ""),
            tonumber(target.x) or 0,
            tonumber(target.y) or 0,
            tostring(target.click_screen_x or ""),
            tostring(target.click_screen_y or ""),
            tostring(target.hwnd or ""),
            tostring(target.click_mode or "")
        ))
    end

    if type(click_fetched_target) == "function" and type(target) == "table" then
        local ok, clicked, click_err, retryable = safe_call(click_fetched_target, step, target)
        if ok then
            return clicked == true, click_err, retryable
        end
        return false, clicked
    end

    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.control_click) ~= "function" then
        return false, "nav.control_click is unavailable."
    end

    if type(target) ~= "table" or tonumber(target.addr) == nil then
        return false, "Invalid locator button target."
    end

    return nav_mod.control_click(target.addr)
end

function M.resolve_window_client_point(ctx, ratio_x, ratio_y)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.window_hwnd) ~= "function" then
        return nil, nil, nil, "nav.window_hwnd is unavailable."
    end

    local hwnd, hwnd_err = nav_mod.window_hwnd()
    if not hwnd then
        return nil, nil, nil, hwnd_err or "game window not found."
    end

    local wnd_api = type(ctx) == "table" and ctx.wnd or wnd
    if type(wnd_api) ~= "table" or type(wnd_api.client_rect) ~= "function" then
        return nil, nil, nil, "wnd.client_rect is unavailable."
    end

    local _, _, client_w, client_h = wnd_api.client_rect(hwnd)
    if type(client_w) ~= "number" or type(client_h) ~= "number" then
        return nil, nil, nil, "wnd.client_rect failed."
    end

    local client_x = math.floor(client_w * (tonumber(ratio_x) or 0.5) + 0.5)
    local client_y = math.floor(client_h * (tonumber(ratio_y) or 0.5) + 0.5)
    return hwnd, client_x, client_y
end

function M.click_window_client_point(ctx, current_time, label, ratio_x, ratio_y, opts)
    local hwnd, client_x, client_y, point_err = M.resolve_window_client_point(ctx, ratio_x, ratio_y)
    if not hwnd then
        return false, point_err
    end

    release_async_combat_inputs(ctx, current_time, true)

    local use_human_center_click = false
    local click_mode = "api"
    if type(opts) == "table" then
        use_human_center_click = opts.center_use_human_mouse == true or opts.use_human_mouse == true
        if opts.center_mouse_mode ~= nil or opts.mouse_mode ~= nil then
            use_human_center_click = true
            click_mode = tostring(opts.center_mouse_mode or opts.mouse_mode or "api")
        end
    end
    if use_human_center_click then
        if click_mode == "" or click_mode == "human" then
            click_mode = "api"
        end
        local click_step = {
            key = tostring(label or "task_entry_center"),
            label = tostring(label or "task_entry_center"),
            fixed_client_click = true,
            fixed_ratio_x = tonumber(ratio_x) or 0.5,
            fixed_ratio_y = tonumber(ratio_y) or 0.5,
            fixed_prefer_ratio = true,
            prefer_screen_click = true,
            mouse_mode = tostring(click_mode),
            click_button = tostring(type(opts) == "table" and (opts.center_click_button or opts.click_button) or "left"),
            click_delay_ms = tonumber(type(opts) == "table" and (opts.center_click_delay_ms or opts.click_delay_ms)) or 50,
            hover_delay_ms = tonumber(type(opts) == "table" and (opts.center_hover_delay_ms or opts.hover_delay_ms)) or 80,
            allow_outside = type(opts) == "table" and opts.center_allow_outside == true
        }
        local target, target_err = M.resolve_fixed_client_click_target(ctx, click_step)
        if type(target) ~= "table" then
            return false, target_err
        end

        local clicked, click_err, retryable = click_locator_button_target(ctx, click_step, target)
        if clicked then
            logger(ctx).info(string.format(
                "[Leveling] task entry center clicked | label=%s client=(%d,%d) screen=(%s,%s) route=human_mouse mode=%s",
                tostring(label or ""),
                tonumber(client_x) or 0,
                tonumber(client_y) or 0,
                tostring(target.click_screen_x or ""),
                tostring(target.click_screen_y or ""),
                tostring(click_mode or "")
            ))
            return true
        end
        return false, click_err or (retryable and "retryable click failed" or "human center click failed.")
    end

    local nav_mod = nav_api(ctx)
    if type(nav_mod) == "table" and type(nav_mod.click_window_to_move) == "function" then
        local clicked, click_err = nav_mod.click_window_to_move(hwnd, client_x, client_y, {
            button = "left",
            delay = 50,
            wait = false
        })
        if clicked then
            logger(ctx).info(string.format(
                "[Leveling] task entry center clicked | label=%s client=(%d,%d)",
                tostring(label or ""),
                tonumber(client_x) or 0,
                tonumber(client_y) or 0
            ))
            return true
        end
        return false, click_err
    end

    local mouse_api = type(ctx) == "table" and ctx.mouse or mouse
    if type(mouse_api) == "table" and type(mouse_api.post_click) == "function" then
        local clicked = mouse_api.post_click(hwnd, client_x, client_y, "left", 50)
        if clicked then
            logger(ctx).info(string.format(
                "[Leveling] task entry center clicked | label=%s client=(%d,%d)",
                tostring(label or ""),
                tonumber(client_x) or 0,
                tonumber(client_y) or 0
            ))
            return true
        end
        return false, "mouse.post_click failed."
    end

    return false, "window center click API is unavailable."
end

function M.maybe_handle_task_entry_action(ctx, current_time, player_x, player_y, player_z)
    local entry_action, task_name = M.current_task_entry_action_config()
    if type(entry_action) ~= "table" or tostring(entry_action.mode or "") ~= "world_map_send" then
        return false
    end

    if type(state.task_target) == "table" then
        return false
    end

    local button_click_at = tonumber(state.last_task_button_click_at) or 0
    local armed_button_click_at = tonumber(state.task_entry_action_button_click_at) or 0
    if armed_button_click_at > 0 then
        if button_click_at > armed_button_click_at and (tonumber(state.task_entry_action_send_clicked_at) or 0) <= 0 then
            log_throttled(ctx, "task_entry_action_ignore_new_button_click", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] task entry action keeps current session, ignore newer task button click | task=%s armed_at=%d newer_at=%d",
                tostring(task_name or state.current_task_name or ""),
                tonumber(armed_button_click_at) or 0,
                tonumber(button_click_at) or 0
            ))
        end
        button_click_at = armed_button_click_at
    end
    if button_click_at <= 0 then
        return false
    end

    local timeout_ms = math.max(5000, tonumber(entry_action.timeout_ms) or 12000)
    local base_map_open_wait_ms = math.max(200, tonumber(entry_action.map_open_wait_ms) or 900)
    local map_open_wait_jitter_ms = math.max(0, tonumber(entry_action.map_open_wait_jitter_ms) or 0)
    local map_open_wait_ms = math.max(
        200,
        tonumber(state.task_entry_action_map_open_wait_ms) or base_map_open_wait_ms
    )
    local center_retry_ms = math.max(400, tonumber(entry_action.center_retry_ms) or 1200)
    local center_settle_ms = math.max(150, tonumber(entry_action.center_settle_ms) or 450)
    local transition_wait_ms = math.max(TASK_BUTTON_SETTLE_MS, tonumber(entry_action.transition_wait_ms) or 1800)
    local action_elapsed_ms = current_time - button_click_at

    if armed_button_click_at <= 0 then
        if map_open_wait_jitter_ms > 0 then
            map_open_wait_ms = base_map_open_wait_ms + math.random(0, math.floor(map_open_wait_jitter_ms))
        else
            map_open_wait_ms = base_map_open_wait_ms
        end
        state.task_entry_action_button_click_at = button_click_at
        state.task_entry_action_center_clicked_at = 0
        state.task_entry_action_map_open_wait_ms = map_open_wait_ms
        state.task_entry_action_next_center_click_at = button_click_at + map_open_wait_ms
        state.task_entry_action_pre_clicked_at = 0
        state.task_entry_action_send_clicked_at = 0
        logger(ctx).info(string.format(
            "[Leveling] task entry action armed | task=%s mode=%s key=%s timeout=%dms map_wait=%dms jitter_max=%dms",
            tostring(task_name or state.current_task_name or ""),
            tostring(entry_action.mode or ""),
            tostring(entry_action.key or ""),
            tonumber(timeout_ms) or 0,
            tonumber(map_open_wait_ms) or 0,
            tonumber(map_open_wait_jitter_ms) or 0
        ))
    end

    if action_elapsed_ms > timeout_ms then
        M.clear_task_entry_action_state()
        log_throttled(ctx, "task_entry_action_timeout", "warn", LOG_THROTTLE_MS, string.format(
            "[Leveling] task entry action timeout, fallback to normal task retry | task=%s mode=%s elapsed=%dms",
            tostring(task_name or state.current_task_name or ""),
            tostring(entry_action.mode or ""),
            tonumber(action_elapsed_ms) or 0
        ))
        return false
    end

    state.stage = "task_entry_action"
    release_async_combat_inputs(ctx, current_time, true)
    hold_navigation(ctx, current_time, "task_entry_action")
    state.task_path_wait_until = math.max(tonumber(state.task_path_wait_until) or 0, current_time + 1000)
    state.next_task_button_click_at = math.max(tonumber(state.next_task_button_click_at) or 0, current_time + 1000)

    if (tonumber(state.task_entry_action_send_clicked_at) or 0) > 0 then
        local send_elapsed_ms = current_time - (tonumber(state.task_entry_action_send_clicked_at) or 0)
        if send_elapsed_ms < transition_wait_ms then
            log_throttled(ctx, "task_entry_action_wait_transition", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] task entry action waiting transition after send | task=%s elapsed=%dms wait=%dms",
                tostring(task_name or state.current_task_name or ""),
                tonumber(send_elapsed_ms) or 0,
                tonumber(transition_wait_ms) or 0
            ))
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end
    end

    if action_elapsed_ms < map_open_wait_ms then
        log_throttled(ctx, "task_entry_action_wait_map", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] task entry action waiting map panel | task=%s mode=%s elapsed=%dms wait=%dms",
                tostring(task_name or state.current_task_name or ""),
            tostring(entry_action.mode or ""),
            tonumber(action_elapsed_ms) or 0,
            tonumber(map_open_wait_ms) or 0
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if (tonumber(state.task_entry_action_center_clicked_at) or 0) <= 0
        and current_time >= (tonumber(state.task_entry_action_next_center_click_at) or 0)
    then
        local clicked_center, center_err = M.click_window_client_point(
            ctx,
            current_time,
            tostring(entry_action.key or entry_action.mode or "task_entry_center"),
            tonumber(entry_action.center_click_ratio_x) or 0.5,
            tonumber(entry_action.center_click_ratio_y) or 0.5,
            entry_action
        )
        if clicked_center then
            local center_clicked_at = now_ms(ctx)
            state.task_entry_action_center_clicked_at = center_clicked_at
            state.task_entry_action_next_center_click_at = center_clicked_at + center_retry_ms
        else
            state.task_entry_action_next_center_click_at = current_time + center_retry_ms
            log_throttled(ctx, "task_entry_action_center_failed", "warn", LOG_THROTTLE_MS,
                "[Leveling] task entry center click failed: " .. tostring(center_err))
        end
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if (tonumber(state.task_entry_action_center_clicked_at) or 0) > 0
        and current_time - (tonumber(state.task_entry_action_center_clicked_at) or 0) < center_settle_ms
    then
        log_throttled(ctx, "task_entry_action_center_settle", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] task entry action waiting center settle | task=%s settle_left=%dms",
            tostring(task_name or state.current_task_name or ""),
            math.max(0, center_settle_ms - (current_time - (tonumber(state.task_entry_action_center_clicked_at) or 0)))
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    local selection_step = type(entry_action.selection_step) == "table" and entry_action.selection_step or nil
    local selection_settle_ms = math.max(150, tonumber(entry_action.selection_settle_ms) or 600)
    if type(selection_step) == "table" and (tonumber(state.task_entry_action_pre_clicked_at) or 0) <= 0 then
        local selection_target, selection_fetch_err = fetch_locator_button_target(ctx, selection_step)
        if type(selection_target) ~= "table" then
            log_throttled(ctx, "task_entry_action_selection_missing", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] task entry selection button not visible yet | task=%s label=%s err=%s",
                tostring(task_name or state.current_task_name or ""),
                tostring(selection_step.label or ""),
                tostring(selection_fetch_err or "")
            ))
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end

        local selection_clicked, selection_click_err, selection_retryable = click_locator_button_target(ctx, selection_step, selection_target)
        if not selection_clicked then
            log_throttled(ctx,
                selection_retryable and "task_entry_action_selection_retry" or "task_entry_action_selection_failed",
                selection_retryable and "info" or "warn",
                LOG_THROTTLE_MS,
                "[Leveling] task entry selection click failed: " .. tostring(selection_click_err))
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end

        state.task_entry_action_pre_clicked_at = current_time
        logger(ctx).info(string.format(
            "[Leveling] task entry selection clicked | task=%s label=%s addr=%s pos=(%s,%s)",
            tostring(task_name or state.current_task_name or ""),
            tostring(selection_step.label or ""),
            tostring(selection_target.addr or ""),
            tostring(selection_target.x or ""),
            tostring(selection_target.y or "")
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if (tonumber(state.task_entry_action_pre_clicked_at) or 0) > 0
        and current_time - (tonumber(state.task_entry_action_pre_clicked_at) or 0) < selection_settle_ms
    then
        log_throttled(ctx, "task_entry_action_selection_settle", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] task entry action waiting selection settle | task=%s settle_left=%dms",
            tostring(task_name or state.current_task_name or ""),
            math.max(0, selection_settle_ms - (current_time - (tonumber(state.task_entry_action_pre_clicked_at) or 0)))
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    local step = type(entry_action.step) == "table" and entry_action.step or nil
    if type(step) ~= "table" then
        log_throttled(ctx, "task_entry_action_step_missing", "warn", LOG_THROTTLE_MS,
            "[Leveling] task entry action step is unavailable.")
        return false
    end

    local target, fetch_err = fetch_locator_button_target(ctx, step)
    if type(target) ~= "table" then
        if current_time >= (tonumber(state.task_entry_action_next_center_click_at) or 0) then
            state.task_entry_action_center_clicked_at = 0
        end
        log_throttled(ctx, "task_entry_action_button_missing", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] task entry send button not visible yet | task=%s label=%s err=%s",
            tostring(task_name or state.current_task_name or ""),
            tostring(step.label or ""),
            tostring(fetch_err or "")
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    local clicked, click_err, retryable = click_locator_button_target(ctx, step, target)
    if not clicked then
        log_throttled(ctx, retryable and "task_entry_action_click_retry" or "task_entry_action_click_failed",
            retryable and "info" or "warn", LOG_THROTTLE_MS,
            "[Leveling] task entry send click failed: " .. tostring(click_err))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    state.task_entry_action_send_clicked_at = current_time
    logger(ctx).info(string.format(
        "[Leveling] task entry send clicked | task=%s label=%s addr=%s pos=(%s,%s)",
        tostring(task_name or state.current_task_name or ""),
        tostring(step.label or ""),
        tostring(target.addr or ""),
        tostring(target.x or ""),
        tostring(target.y or "")
    ))
    schedule_task_refresh_after_transition(ctx, current_time, "task_entry_world_map_send", transition_wait_ms, {
        force_task_call = true,
        task_pos_reject_extra_ms = 3500
    })
    log_heartbeat(ctx, current_time, player_x, player_y, player_z)
    return true
end

local function try_click_revive_checkpoint(ctx, current_time)
    local revive_steps = {
        REVIVE_REENTER_STEP,
        REVIVE_AT_CHECKPOINT_STEP
    }
    local last_err = nil

    for _, step in ipairs(revive_steps) do
        local target, fetch_err = fetch_locator_button_target(ctx, step)
        if target then
            local clicked, click_err, retryable = click_locator_button_target(ctx, step, target)
            if clicked then
                state.revive_clicked_at = current_time
                state.revive_click_count = (tonumber(state.revive_click_count) or 0) + 1
                state.next_revive_click_at = current_time + REVIVE_CLICK_RETRY_INTERVAL_MS
                logger(ctx).info(string.format(
                    "[Leveling] revive button clicked | label=%s addr=%s pos=(%s,%s) clicks=%d",
                    tostring(step.label or ""),
                    tostring(type(target) == "table" and target.addr or ""),
                    tostring(type(target) == "table" and target.x or ""),
                    tostring(type(target) == "table" and target.y or ""),
                    tonumber(state.revive_click_count) or 0
                ))
                return true
            end

            last_err = click_err or fetch_err
            if retryable == false then
                break
            end
        else
            last_err = fetch_err or last_err
        end
    end

    state.next_revive_click_at = current_time + REVIVE_CLICK_RETRY_INTERVAL_MS
    log_throttled(ctx, "revive_fetch_failed", "warn", LOG_THROTTLE_MS,
        "[Leveling] revive button fetch failed: " .. tostring(last_err))
    return false, last_err
end

local function find_interaction_prompt_button(ctx, current_time)
    current_time = tonumber(current_time) or now_ms(ctx)
    if current_time < (tonumber(state.next_interaction_prompt_scan_at) or 0) then
        return state.cached_interaction_prompt_target, state.cached_interaction_prompt_error
    end

    state.next_interaction_prompt_scan_at = current_time + INTERACTION_PROMPT_SCAN_INTERVAL_MS

    local target, fetch_err = fetch_locator_button_target(ctx, INTERACTION_PROMPT_STEP)
    if target then
        state.cached_interaction_prompt_target = target
        state.cached_interaction_prompt_error = nil
        return target, nil
    end

    state.cached_interaction_prompt_target = nil
    state.cached_interaction_prompt_error = fetch_err or "interaction prompt not found."
    return nil, state.cached_interaction_prompt_error
end

local function find_exit_portal_button(ctx, current_time)
    current_time = tonumber(current_time) or now_ms(ctx)
    if current_time < (tonumber(state.next_exit_portal_scan_at) or 0) then
        return state.cached_exit_portal_target, state.cached_exit_portal_error
    end

    state.next_exit_portal_scan_at = current_time + EXIT_PORTAL_SCAN_INTERVAL_MS

    local target, fetch_err = fetch_locator_button_target(ctx, EXIT_PORTAL_STEP)
    if target then
        state.cached_exit_portal_target = target
        state.cached_exit_portal_error = nil
        return target, nil
    end

    state.cached_exit_portal_target = nil
    state.cached_exit_portal_error = fetch_err or "exit portal button not found."
    return nil, state.cached_exit_portal_error
end

function M.find_task_objective_button(ctx, current_time, objective_cfg)
    current_time = tonumber(current_time) or now_ms(ctx)
    local objective_key = tostring(type(objective_cfg) == "table" and objective_cfg.key or "")
    if current_time < (tonumber(state.next_task_objective_button_scan_at) or 0)
        and tostring(state.cached_task_objective_button_key or "") == objective_key
    then
        return state.cached_task_objective_button, state.cached_task_objective_button_error
    end

    state.next_task_objective_button_scan_at = current_time + 250
    state.cached_task_objective_button_key = objective_key

    local steps = {}
    local has_objective_steps = false
    if type(objective_cfg) == "table" then
        if type(objective_cfg.button_step) == "table" then
            steps[#steps + 1] = objective_cfg.button_step
            has_objective_steps = true
        end
        if type(objective_cfg.button_steps) == "table" then
            for _, step in ipairs(objective_cfg.button_steps) do
                if type(step) == "table" then
                    steps[#steps + 1] = step
                    has_objective_steps = true
                end
            end
        end
    end
    if not has_objective_steps
        or (type(objective_cfg) == "table" and objective_cfg.include_global_button_steps == true)
    then
        for _, step in ipairs(M.TASK_OBJECTIVE_BUTTON_STEPS or {}) do
            if type(step) == "table" then
                steps[#steps + 1] = step
            end
        end
    end

    for _, step in ipairs(steps) do
        local target, fetch_err = fetch_locator_button_target(ctx, step)
        if target then
            local result = {
                step = step,
                target = target,
                arm_task_entry_action_after_click = (
                    (type(objective_cfg) == "table" and objective_cfg.arm_task_entry_action_after_click == true)
                    or (type(step) == "table" and step.arm_task_entry_action_after_click == true)
                ),
                force_task_call_after_transition = type(objective_cfg) == "table"
                    and objective_cfg.force_task_call_after_transition == true,
                task_pos_reject_extra_ms = type(objective_cfg) == "table"
                    and objective_cfg.task_pos_reject_extra_ms
                    or nil
            }
            state.cached_task_objective_button = result
            state.cached_task_objective_button_error = nil
            return result, nil
        end
        state.cached_task_objective_button_error = fetch_err or state.cached_task_objective_button_error
    end

    state.cached_task_objective_button = nil
    state.cached_task_objective_button_error = state.cached_task_objective_button_error or "task objective button not found."
    return nil, state.cached_task_objective_button_error
end

function M.try_click_task_objective_button(ctx, current_time, objective_button, goal_distance, target_source)
    local step = type(objective_button) == "table" and objective_button.step or nil
    local target = type(objective_button) == "table" and objective_button.target or nil
    if type(step) ~= "table" or type(target) ~= "table" then
        return false, "task objective button target is unavailable."
    end

    if current_time < (tonumber(state.next_task_objective_button_click_at) or 0) then
        return false, "task objective button click cooldown."
    end

    release_async_combat_inputs(ctx, current_time, true)
    local clicked, click_err, retryable = click_locator_button_target(ctx, step, target)
    if not clicked then
        log_throttled(ctx, retryable and "task_objective_button_click_retry" or "task_objective_button_click_failed",
            retryable and "info" or "warn", LOG_THROTTLE_MS,
            "[Leveling] task objective button click failed: " .. tostring(click_err))
        state.next_task_objective_button_click_at = current_time + 600
        return false, click_err
    end

    local after_click_time = now_ms(ctx)
    state.next_task_objective_button_click_at = after_click_time + 1200
    local direct_post_flow, direct_post_task_name = M.current_task_post_dialogue_flow_config()
    if type(direct_post_flow) == "table"
        and direct_post_flow.arm_after_objective_button == true
        and M.arm_post_dialogue_flow(ctx, after_click_time, direct_post_flow, direct_post_task_name, "task_objective_button") == true
    then
        clear_pending_interaction()
        state.require_task_button_refresh = false
        state.require_task_button_refresh_reason = nil
        state.task_update_wait_until = math.max(
            tonumber(state.task_update_wait_until) or 0,
            after_click_time + math.max(tonumber(direct_post_flow.initial_delay_ms) or 500, 300)
        )
        state.next_task_button_click_at = math.max(
            tonumber(state.next_task_button_click_at) or 0,
            state.task_update_wait_until
        )
        state.next_task_refresh_at = math.max(
            tonumber(state.next_task_refresh_at) or 0,
            state.task_update_wait_until
        )
        logger(ctx).info(string.format(
            "[Leveling] task objective button opened NPC dialogue; direct post flow armed | task=%s key=%s label=%s",
            tostring(direct_post_task_name or state.current_task_name or ""),
            tostring(direct_post_flow.key or ""),
            tostring(step.label or "")
        ))
        return true
    end

    if type(objective_button) == "table" and objective_button.arm_task_entry_action_after_click == true then
        local armed_entry_action, arm_err = M.arm_task_entry_action_after_task_objective(
            ctx,
            after_click_time,
            objective_button
        )
        if armed_entry_action == true then
            local entry_action = select(1, M.current_task_entry_action_config())
            logger(ctx).info(string.format(
                "[Leveling] task objective button armed task entry action | label=%s entry_key=%s",
                tostring(step.label or ""),
                tostring(type(entry_action) == "table" and entry_action.key or "")
            ))
            return true
        end
        log_throttled(
            ctx,
            "task_objective_button_arm_task_entry_action_failed_" .. tostring(step.key or step.label or ""),
            "warn",
            LOG_THROTTLE_MS,
            "[Leveling] task objective button could not arm task entry action: " .. tostring(arm_err)
        )
    end

    local refresh_opts = nil
    if step.force_task_call_after_transition == true
        or objective_button.force_task_call_after_transition == true
    then
        refresh_opts = {
            force_task_call = true,
            task_pos_reject_extra_ms = tonumber(step.task_pos_reject_extra_ms)
                or tonumber(objective_button.task_pos_reject_extra_ms)
                or 2500
        }
    end
    schedule_task_refresh_after_transition(
        ctx,
        current_time,
        "task_objective_button_" .. tostring(step.key or step.label or ""),
        tonumber(step.settle_ms) or POST_DIALOGUE_SETTLE_MS,
        refresh_opts
    )
    logger(ctx).info(string.format(
        "[Leveling] task objective button clicked | label=%s goal_distance=%.2f target_source=%s pos=%.2f, %.2f",
        tostring(step.label or ""),
        tonumber(goal_distance) or 0,
        tostring(target_source or ""),
        tonumber(target.x) or 0,
        tonumber(target.y) or 0
    ))
    return true
end

function M.maybe_handle_task_objective_button(ctx, current_time, goal_distance, target_source, objective_cfg)
    local objective_button = M.find_task_objective_button(ctx, current_time, objective_cfg)
    if not objective_button then
        return false
    end

    state.stage = "task_objective_button"
    hold_navigation(ctx, current_time, "task_objective_button")
    return M.try_click_task_objective_button(
        ctx,
        current_time,
        objective_button,
        goal_distance,
        target_source
    )
end

function M.maybe_handle_task_reached_prompt_or_portal(ctx, current_time, goal_distance, target_source)
    local interaction_prompt = find_interaction_prompt_button(ctx, current_time)
    if interaction_prompt then
        state.stage = "interaction_prompt"
        hold_navigation(ctx, current_time, "interaction_prompt")
        M.interact_with_prompt(
            ctx,
            current_time,
            interaction_prompt,
            goal_distance,
            target_source
        )
        return true
    end

    local exit_portal_target = find_exit_portal_button(ctx, current_time)
    if exit_portal_target then
        state.stage = "exit_portal"
        hold_navigation(ctx, current_time, "exit_portal")
        if try_click_exit_portal(
            ctx,
            current_time,
            exit_portal_target,
            goal_distance,
            target_source
        ) then
            return true
        end
    end

    return false
end

function M.normalize_kite_template_points(points, fallback_z)
    if type(points) ~= "table" then
        return nil
    end

    local normalized = {}
    for _, point in ipairs(points) do
        local point_x = tonumber(type(point) == "table" and point.x)
        local point_y = tonumber(type(point) == "table" and point.y)
        if point_x ~= nil and point_y ~= nil then
            normalized[#normalized + 1] = {
                x = point_x,
                y = point_y,
                z = tonumber(type(point) == "table" and point.z) or tonumber(fallback_z)
            }
        end
    end

    if #normalized < 3 then
        return nil
    end
    return normalized
end

function M.apply_task_combat_kite_runtime_options(objective_cfg, point_objective_cfg)
    local point_cfg = type(point_objective_cfg) == "table" and point_objective_cfg or nil
    local task_cfg = type(objective_cfg) == "table" and objective_cfg or nil
    local seamless = (point_cfg and point_cfg.seamless_kite == true) or (task_cfg and task_cfg.seamless_kite == true)
    local async_override = nil
    if point_cfg and point_cfg.async_route_worker ~= nil then
        async_override = point_cfg.async_route_worker
    elseif task_cfg and task_cfg.async_route_worker ~= nil then
        async_override = task_cfg.async_route_worker
    end
    local has_configured_kite_points = type(point_cfg and point_cfg.kite_points) == "table"
        or type(task_cfg and task_cfg.kite_points) == "table"
    local endpoint_kite = (point_cfg and point_cfg.immediate_kite_on_reached == true)
        or (task_cfg and task_cfg.immediate_kite_on_reached == true)

    state.task_combat_kite_seamless = seamless == true
    state.task_combat_kite_switch_ms = tonumber(point_cfg and point_cfg.kite_switch_ms)
        or tonumber(task_cfg and task_cfg.kite_switch_ms)
        or tonumber(state.task_combat_kite_switch_ms)
    state.task_combat_kite_async_worker = M.TASK_COMBAT_KITE_ASYNC_ROUTE_WORKER == true
        and async_override ~= false
        and (
            async_override == true
            or seamless == true
            or has_configured_kite_points == true
            or endpoint_kite == true
        )
    state.task_combat_kite_arrive_distance = tonumber(point_cfg and (point_cfg.kite_arrive_distance or point_cfg.kite_point_arrive_distance))
        or tonumber(task_cfg and (task_cfg.kite_arrive_distance or task_cfg.kite_point_arrive_distance))
    state.task_combat_kite_move_interval_ms = tonumber(point_cfg and point_cfg.kite_move_interval_ms)
        or tonumber(task_cfg and task_cfg.kite_move_interval_ms)

    if seamless then
        state.task_combat_kite_arrive_distance = tonumber(state.task_combat_kite_arrive_distance) or 520
        state.task_combat_kite_move_interval_ms = tonumber(state.task_combat_kite_move_interval_ms) or 180
    end
end

function M.arm_immediate_task_boss_kite(ctx, current_time, task_name, objective_cfg, point_objective_cfg, destination, target, player_x, player_y, player_z, reason)
    if type(objective_cfg) ~= "table" or tostring(objective_cfg.mode or "") ~= "boss_kite" then
        return false
    end

    state.task_reached_unresolved_since = 0
    state.task_combat_force_kite = true
    if (tonumber(state.task_combat_started_at) or 0) <= 0 then
        state.task_combat_started_at = current_time
    end
    M.lock_boss_combat_context(task_name, objective_cfg, point_objective_cfg)

    local anchor = nil
    if type(point_objective_cfg) == "table"
        and tonumber(point_objective_cfg.x) ~= nil
        and tonumber(point_objective_cfg.y) ~= nil
    then
        anchor = point_objective_cfg
    elseif is_valid_world_point(destination) then
        anchor = destination
    elseif is_valid_world_point(target) then
        anchor = target
    end

    state.task_combat_anchor_x = tonumber(anchor and anchor.x) or tonumber(player_x)
    state.task_combat_anchor_y = tonumber(anchor and anchor.y) or tonumber(player_y)
    state.task_combat_anchor_z = tonumber(anchor and anchor.z) or tonumber(player_z)
    state.task_combat_kite_radius = tonumber(type(point_objective_cfg) == "table" and point_objective_cfg.kite_radius)
        or tonumber(objective_cfg.kite_radius)
        or tonumber(state.task_combat_kite_radius)
    state.task_combat_kite_template_points = M.normalize_kite_template_points(
        type(point_objective_cfg) == "table" and point_objective_cfg.kite_points or objective_cfg.kite_points,
        state.task_combat_anchor_z
    )
    state.task_combat_kite_switch_ms = tonumber(type(point_objective_cfg) == "table" and point_objective_cfg.kite_switch_ms)
        or tonumber(objective_cfg.kite_switch_ms)
    M.apply_task_combat_kite_runtime_options(objective_cfg, point_objective_cfg)
    state.task_combat_kite_points = nil
    state.task_combat_kite_index = 0
    state.task_combat_next_kite_switch_at = 0
    state.task_combat_kite_force_move = true

    local kite_target = M.build_task_combat_kite_target(current_time, player_x, player_y, destination or target)
    state.stage = "task_combat_kite"
    if type(kite_target) == "table" then
        M.issue_task_combat_kite_move(ctx, current_time, kite_target)
    end
    issue_combat_pulse(ctx, current_time, "immediate_boss_kite", true)
    log_throttled(ctx, "task_immediate_boss_kite", "info", LOG_THROTTLE_MS, string.format(
        "[Leveling] immediate boss kite armed at objective | task=%s objective=%s reason=%s kite_target=%s route_index=%s/%s",
        tostring(task_name or state.current_task_name or ""),
        tostring(objective_cfg.key or ""),
        tostring(reason or ""),
        type(kite_target) == "table" and string.format("%.2f, %.2f", tonumber(kite_target.x) or 0, tonumber(kite_target.y) or 0) or "nil",
        type(kite_target) == "table" and tostring(kite_target.path_index or "") or "",
        type(kite_target) == "table" and tostring(kite_target.path_points or "") or ""
    ))
    return true
end

function M.maybe_handle_task_reached(
    ctx,
    current_time,
    player_x,
    player_y,
    player_z,
    hp_ratio,
    target,
    destination,
    destination_distance,
    goal_distance
)
    state.stage = "task_reached"
    state.next_task_refresh_at = math.min(
        tonumber(state.next_task_refresh_at) or current_time,
        current_time + 300
    )

    local target_source = tostring(destination and destination.source or target.source or "")
    local map_cfg, map_name = current_map_task_config()
    local task_cfg, task_name, task_cfg_source = M.current_task_runtime_config()
    local task_detail = M.current_task_log_detail()
    local point_objective_cfg = M.current_objective_point_config(destination, target)
    local objective_cfg = type(task_cfg) == "table" and type(task_cfg.objective) == "table" and task_cfg.objective
        or (type(map_cfg) == "table" and type(map_cfg.objective) == "table" and map_cfg.objective or nil)
        or point_objective_cfg
    local objective_source = type(task_cfg) == "table" and type(task_cfg.objective) == "table" and "task"
        or (type(map_cfg) == "table" and type(map_cfg.objective) == "table" and "map" or nil)
        or (type(point_objective_cfg) == "table" and "point" or "none")
    local sticky_force_boss = state.task_combat_force_kite == true
        and (tonumber(state.task_combat_last_seen_at) or 0) > 0
        and current_time - (tonumber(state.task_combat_last_seen_at) or 0) <= TASK_COMBAT_CLEAR_SETTLE_MS
    if state.task_combat_force_kite == true and not sticky_force_boss then
        state.task_combat_force_kite = false
    end
    local objective_mode = type(objective_cfg) == "table" and tostring(objective_cfg.mode or "") or ""
    local force_boss_objective = objective_mode == "boss_kite"
        or sticky_force_boss
    local boss_trigger_distance = type(M._leveling_policy) == "table"
        and type(M._leveling_policy.objective_ready_distance) == "function"
        and M._leveling_policy.objective_ready_distance(TARGET_REACHED_DISTANCE, objective_cfg)
        or math.max(
            TARGET_REACHED_DISTANCE,
            tonumber(type(objective_cfg) == "table" and force_boss_objective and objective_cfg.trigger_distance) or 360
        )
    local skip_direct_interact = (type(objective_cfg) == "table" and objective_cfg.skip_direct_interact == true)
        and (
            force_boss_objective
            or objective_mode == "task_objective_button"
        )
        or sticky_force_boss

    log_throttled(ctx, "task_reached_evaluate", "info", LOG_THROTTLE_MS, string.format(
        "[Leveling] task reached evaluate | map=%s task=%s detail=%s task_cfg_source=%s source=%s goal_distance=%.2f destination_distance=%s objective_source=%s objective_key=%s objective_mode=%s force_boss=%s sticky_boss=%s skip_direct_interact=%s",
        tostring(map_name or ""),
        tostring(task_name or state.current_task_name or ""),
        tostring(task_detail or ""),
        tostring(task_cfg_source or ""),
        tostring(target_source or ""),
        tonumber(goal_distance) or 0,
        type(destination_distance) == "number" and string.format("%.2f", destination_distance) or "nil",
        tostring(objective_source or "none"),
        type(objective_cfg) == "table" and tostring(objective_cfg.key or "") or "",
        objective_mode,
        force_boss_objective and "true" or "false",
        sticky_force_boss and "true" or "false",
        skip_direct_interact and "true" or "false"
    ))

    M.ensure_terminal_task_lock(task_name, objective_cfg)
    if M.maybe_handle_terminal_task_change(ctx, current_time) then
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if force_boss_objective
        and type(objective_cfg) == "table"
        and objective_cfg.immediate_kite_on_reached == true
        and type(goal_distance) == "number"
        and goal_distance <= boss_trigger_distance
        and M.arm_immediate_task_boss_kite(
            ctx,
            current_time,
            task_name,
            objective_cfg,
            point_objective_cfg,
            destination,
            target,
            player_x,
            player_y,
            player_z,
            "task_reached"
        )
    then
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    local recent_combat_gate_active = false
    local recent_combat_idle_ms = current_time - (tonumber(state.task_combat_last_seen_at) or 0)
    if (tonumber(state.task_combat_last_seen_at) or 0) > 0
        and recent_combat_idle_ms <= TASK_COMBAT_CLEAR_SETTLE_MS
        and (tonumber(state.task_combat_last_count) or 0) > 0
        and state.task_combat_force_kite ~= true
    then
        recent_combat_gate_active = true
        log_throttled(ctx, "task_reached_recent_combat_gate", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] recent combat at objective, skip prompt/dialogue checks | idle_ms=%d last_count=%d stage=%s",
            tonumber(recent_combat_idle_ms) or 0,
            tonumber(state.task_combat_last_count) or 0,
            tostring(state.stage or "")
        ))
    end

    if M.maybe_handle_low_priority_task_ui(ctx, current_time, player_x, player_y, player_z, {
        phase = "task_reached",
        has_target = true,
        goal_distance = goal_distance,
        objective_ready_distance = boss_trigger_distance
    }) then
        return true
    end

    if not recent_combat_gate_active
        and M.maybe_handle_task_objective_button(ctx, current_time, goal_distance, target_source, objective_cfg)
    then
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if not recent_combat_gate_active
        and M.maybe_handle_task_reached_prompt_or_portal(ctx, current_time, goal_distance, target_source)
    then
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if tostring(target.source or "") == "task_pos"
        and (type(state.task_path) ~= "table" or #state.task_path == 0)
        and is_valid_world_point(state.task_pos)
        and type(player_z) == "number"
        and math.abs((tonumber(state.task_pos.z) or 0) - tonumber(player_z)) >= 300
    then
        schedule_task_refresh_after_transition(ctx, current_time, "task_pos_vertical_mismatch", TASK_BUTTON_SETTLE_MS)
        log_throttled(ctx, "task_pos_vertical_mismatch", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] task objective appears completed/unreachable after map update, refresh next task | z_gap=%.2f target=%.2f, %.2f, %.2f",
            math.abs((tonumber(state.task_pos.z) or 0) - tonumber(player_z)),
            tonumber(state.task_pos.x) or 0,
            tonumber(state.task_pos.y) or 0,
            tonumber(state.task_pos.z) or 0
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if is_valid_world_point(state.task_pos)
        and type(destination_distance) == "number"
        and destination_distance > TASK_INTERACTION_APPROACH_DISTANCE
    then
        local precise_target = {
            x = tonumber(state.task_pos.x),
            y = tonumber(state.task_pos.y),
            z = tonumber(state.task_pos.z),
            source = "task_pos_precise",
            path_index = 0,
            path_points = tonumber(state.task_path_count) or 0,
            move_interval_ms = M.TASK_POS_MOVE_INTERVAL_MS
        }
        local objective_monsters, objective_monster_err = M.find_task_monsters(ctx, current_time, player_x, player_y)
        local objective_pulse_state = "none"
        if objective_monsters and tonumber(objective_monsters.count) and tonumber(objective_monsters.count) > 0 then
            local objective_nearest_monster = objective_monsters.nearest or {}
            local objective_nearest_distance = tonumber(objective_nearest_monster.distance)
            local should_objective_pulse, objective_pulse_reason = should_issue_nearby_monster_pulse(current_time)
            objective_pulse_state = tostring(objective_pulse_reason or "none")
            if should_objective_pulse
                and objective_nearest_distance ~= nil
                and objective_nearest_distance <= TASK_MONSTER_PLAYER_DISTANCE
            then
                local pulse_ok, pulse_err = issue_combat_pulse(ctx, current_time, "nearby_monster", true)
                if pulse_ok then
                    objective_pulse_state = "issued"
                else
                    objective_pulse_state = "blocked:" .. tostring(pulse_err or "unknown")
                end
            elseif objective_nearest_distance == nil
                or objective_nearest_distance > TASK_MONSTER_PLAYER_DISTANCE
            then
                objective_pulse_state = "monster_too_far"
            end
        elseif objective_monster_err then
            objective_pulse_state = "scan_miss"
        end

        state.task_reached_unresolved_since = 0
        state.stage = "approach_task_objective"
        issue_move(ctx, current_time, precise_target)
        log_throttled(ctx, "task_precise_approach", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] objective not close enough for interaction, continue exact approach | distance=%.2f source=%s pulse=%s",
            tonumber(destination_distance) or 0,
            target_source,
            tostring(objective_pulse_state or "none")
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    local nearest_npc, npc_err = nil, nil
    if not recent_combat_gate_active then
        nearest_npc, npc_err = M.find_current_task_npc(ctx, current_time, player_x, player_y)
    end
    if nearest_npc and nearest_npc.distance <= NPC_DIALOGUE_TRIGGER_DISTANCE then
        state.task_reached_unresolved_since = 0
        state.stage = "npc_dialogue"
        hold_navigation(ctx, current_time, "npc_dialogue")
        M.interact_with_npc(ctx, current_time, nearest_npc)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if not recent_combat_gate_active
        and not skip_direct_interact
        and is_valid_world_point(state.task_pos)
        and type(destination_distance) == "number"
        and destination_distance <= 115
        and current_time - (tonumber(state.last_dialogue_at) or 0) >= DIALOGUE_COOLDOWN_MS
    then
        state.task_reached_unresolved_since = 0
        state.stage = "interaction_prompt"
        hold_navigation(ctx, current_time, "task_objective_direct_interact")
        M.interact_with_prompt(ctx, current_time, {
            related_text = "task_objective_direct",
            name = "task objective direct interact",
            x = tonumber(state.task_pos.x) or 0,
            y = tonumber(state.task_pos.y) or 0
        }, goal_distance, target_source, false, false)
        log_throttled(ctx, "task_objective_direct_interact", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] direct objective interact attempted before combat | distance=%.2f source=%s",
            tonumber(destination_distance) or 0,
            target_source
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    local task_monsters, monster_err = M.find_task_monsters(ctx, current_time, player_x, player_y)
    if task_monsters and tonumber(task_monsters.count) and task_monsters.count > 0 then
        local nearest_monster = task_monsters.nearest or {}
        local forced_kite_monster = M.find_forced_kite_monster(task_monsters)
        local nearest_monster_distance = tonumber(nearest_monster.distance)
        local nearest_task_distance = tonumber(nearest_monster.task_distance)
        local map_force_boss_ready = force_boss_objective
            and type(destination_distance) == "number"
            and destination_distance <= boss_trigger_distance
            and (
                type(task_monsters.nearest_special) == "table"
                or objective_cfg.allow_any_monster == true
            )
        if type(forced_kite_monster) == "table" then
            map_force_boss_ready = true
        end
        local objective_monster_ready = (nearest_task_distance ~= nil and nearest_task_distance <= TASK_MONSTER_TARGET_DISTANCE)
            or (
                type(destination_distance) == "number"
                and destination_distance <= TASK_INTERACTION_APPROACH_DISTANCE
                and nearest_monster_distance ~= nil
                and nearest_monster_distance <= TASK_MONSTER_PLAYER_DISTANCE
            )
            or map_force_boss_ready
        local recent_path_move_guard = tostring(target.source or "") == "task_path"
            and (tonumber(state.last_move_call_at) or 0) > 0
            and current_time - (tonumber(state.last_move_call_at) or 0) < 1400

        if tostring(target.source or "") == "task_path"
            and (#(state.task_path or {}) > 0)
            and not map_force_boss_ready
            and (not objective_monster_ready or recent_path_move_guard)
        then
            state.task_reached_unresolved_since = 0
            state.stage = "follow_task"
            if current_time >= (tonumber(state.next_move_at) or 0) then
                issue_move(ctx, current_time, target)
            end
            local should_path_pulse, path_pulse_reason = should_issue_nearby_monster_pulse(current_time)
            local path_pulse_state = tostring(path_pulse_reason or "none")
            if should_path_pulse and nearest_monster_distance ~= nil
                and nearest_monster_distance <= TASK_MONSTER_PLAYER_DISTANCE
            then
                local pulse_ok, pulse_err = issue_combat_pulse(ctx, current_time, "nearby_monster", true)
                if pulse_ok then
                    path_pulse_state = "issued"
                else
                    path_pulse_state = "blocked:" .. tostring(pulse_err or "unknown")
                end
            elseif nearest_monster_distance == nil or nearest_monster_distance > TASK_MONSTER_PLAYER_DISTANCE then
                path_pulse_state = "monster_too_far"
            end
            log_throttled(ctx, "task_combat_deferred_until_destination", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] monsters seen before objective is ready, continue task path | goal_distance=%.2f nearest_distance=%s task_distance=%s recent_move_guard=%s pulse=%s count=%d",
                tonumber(goal_distance) or 0,
                nearest_monster_distance ~= nil and string.format("%.2f", nearest_monster_distance) or "nil",
                nearest_task_distance ~= nil and string.format("%.2f", nearest_task_distance) or "nil",
                recent_path_move_guard and "true" or "false",
                tostring(path_pulse_state or "none"),
                tonumber(task_monsters.count) or 0
            ))
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end

        log_throttled(ctx, "task_reached_monster_gate", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] task reached monster gate | map=%s task=%s source=%s monsters=%d nearest_distance=%s task_distance=%s objective_ready=%s boss_ready=%s has_special=%s allow_any_monster=%s special_label=%s special_kind=%s forced_label=%s",
            tostring(map_name or ""),
            tostring(task_name or state.current_task_name or ""),
            tostring(target_source or ""),
            tonumber(task_monsters.count) or 0,
            nearest_monster_distance ~= nil and string.format("%.2f", nearest_monster_distance) or "nil",
            nearest_task_distance ~= nil and string.format("%.2f", nearest_task_distance) or "nil",
            objective_monster_ready and "true" or "false",
            map_force_boss_ready and "true" or "false",
            type(task_monsters.nearest_special) == "table" and "true" or "false",
            type(objective_cfg) == "table" and objective_cfg.allow_any_monster == true and "true" or "false",
            tostring(task_monsters.nearest_special and task_monsters.nearest_special.label or ""),
            tostring(task_monsters.nearest_special and task_monsters.nearest_special.special_kind or ""),
            tostring(forced_kite_monster and forced_kite_monster.label or "")
        ))
        M.mark_task_combat_seen(current_time, player_x, player_y, destination or target, task_monsters)
        if map_force_boss_ready then
            state.task_combat_force_kite = true
            M.lock_boss_combat_context(task_name, objective_cfg, point_objective_cfg)
            local boss_anchor = forced_kite_monster or task_monsters.nearest_special or task_monsters.nearest or nil
            state.task_combat_kite_template_points = nil
            state.task_combat_kite_switch_ms = nil
            M.apply_task_combat_kite_runtime_options(objective_cfg, point_objective_cfg)
            local configured_kite_points = type(point_objective_cfg) == "table" and point_objective_cfg.kite_points
                or type(objective_cfg) == "table" and objective_cfg.kite_points
                or nil
            if type(configured_kite_points) == "table" then
                local normalized_points = {}
                for _, point in ipairs(configured_kite_points) do
                    local point_x = tonumber(type(point) == "table" and point.x)
                    local point_y = tonumber(type(point) == "table" and point.y)
                    if point_x ~= nil and point_y ~= nil then
                        normalized_points[#normalized_points + 1] = {
                            x = point_x,
                            y = point_y,
                            z = tonumber(type(point) == "table" and point.z) or tonumber(state.task_combat_anchor_z)
                        }
                    end
                end
                if #normalized_points >= 3 then
                    state.task_combat_kite_template_points = normalized_points
                    state.task_combat_kite_switch_ms = tonumber(type(point_objective_cfg) == "table" and point_objective_cfg.kite_switch_ms)
                        or tonumber(type(objective_cfg) == "table" and objective_cfg.kite_switch_ms)
                end
            end
            if type(state.task_combat_kite_points) ~= "table" then
                if type(point_objective_cfg) == "table"
                    and tostring(point_objective_cfg.mode or "") == "boss_kite"
                    and tonumber(point_objective_cfg.x) ~= nil
                    and tonumber(point_objective_cfg.y) ~= nil
                then
                    state.task_combat_anchor_x = tonumber(point_objective_cfg.x)
                    state.task_combat_anchor_y = tonumber(point_objective_cfg.y)
                    state.task_combat_anchor_z = tonumber(point_objective_cfg.z) or tonumber(state.task_combat_anchor_z)
                    state.task_combat_kite_radius = tonumber(point_objective_cfg.kite_radius)
                        or tonumber(objective_cfg and objective_cfg.kite_radius)
                        or tonumber(state.task_combat_kite_radius)
                elseif type(boss_anchor) == "table" then
                    state.task_combat_anchor_x = tonumber(boss_anchor.x) or tonumber(state.task_combat_anchor_x)
                    state.task_combat_anchor_y = tonumber(boss_anchor.y) or tonumber(state.task_combat_anchor_y)
                    state.task_combat_anchor_z = tonumber(boss_anchor.z) or tonumber(state.task_combat_anchor_z)
                    state.task_combat_kite_radius = tonumber(objective_cfg and objective_cfg.kite_radius)
                        or tonumber(state.task_combat_kite_radius)
                end
            end
        end

        local hard_kite = map_force_boss_ready or M.should_use_task_combat_kiting(current_time, task_monsters, hp_ratio)
        local special_monster = forced_kite_monster or task_monsters.nearest_special or task_monsters.nearest or {}
        local kite_target = hard_kite and M.build_task_combat_kite_target(current_time, player_x, player_y, destination or target) or nil
        if hard_kite and type(kite_target) == "table" then
            state.task_reached_unresolved_since = 0
            state.stage = "task_combat_kite"
            M.issue_task_combat_kite_move(ctx, current_time, kite_target)
            issue_combat_pulse(ctx, current_time, "task_combat_kite", true)
            log_throttled(ctx, "task_combat_kiting", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] task combat kiting | map=%s task=%s monsters=%d special=%s special_kind=%s nearest_distance=%.2f task_distance=%s hp_ratio=%s anchor=%.2f, %.2f kite_target=%.2f, %.2f route_index=%d/%d hard=%s boss_map=%s",
                tostring(map_name or ""),
                tostring(task_name or state.current_task_name or ""),
                tonumber(task_monsters.count) or 0,
                tostring(special_monster.label or ""),
                tostring(special_monster.special_kind or ""),
                nearest_monster_distance or 0,
                nearest_task_distance ~= nil and string.format("%.2f", nearest_task_distance) or "nil",
                type(hp_ratio) == "number" and string.format("%.2f", tonumber(hp_ratio) or 0) or "nil",
                tonumber(state.task_combat_anchor_x) or 0,
                tonumber(state.task_combat_anchor_y) or 0,
                tonumber(kite_target.x) or 0,
                tonumber(kite_target.y) or 0,
                tonumber(kite_target.path_index) or 0,
                tonumber(kite_target.path_points) or 0,
                hard_kite and "true" or "false",
                map_force_boss_ready and "true" or "false"
            ))
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end

        state.task_reached_unresolved_since = 0
        state.stage = "task_combat"
        log_throttled(ctx, "task_objective_combat", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] task objective inferred | type=combat map=%s task=%s monsters=%d nearest=%s player_distance=%.2f task_distance=%s hard=%s special=%s special_kind=%s boss_map=%s",
            tostring(map_name or ""),
            tostring(task_name or state.current_task_name or ""),
            tonumber(task_monsters.count) or 0,
            tostring(nearest_monster.label or ""),
            tonumber(nearest_monster.distance) or 0,
            nearest_monster.task_distance ~= nil and string.format("%.2f", tonumber(nearest_monster.task_distance) or 0) or "nil",
            hard_kite and "true" or "false",
            tostring(special_monster.label or ""),
            tostring(special_monster.special_kind or ""),
            map_force_boss_ready and "true" or "false"
        ))
        issue_combat_pulse(ctx, current_time, "task_combat", true)
        log_throttled(ctx, "task_reached", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] near task target | distance=%.2f source=%s",
            goal_distance,
            target_source
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if (tonumber(state.task_combat_last_seen_at) or 0) > 0 then
        local combat_idle_ms = current_time - (tonumber(state.task_combat_last_seen_at) or 0)
        if combat_idle_ms >= TASK_COMBAT_CLEAR_SETTLE_MS then
            state.task_reached_unresolved_since = 0
            schedule_task_refresh_after_transition(ctx, current_time, "task_combat_cleared", POST_DIALOGUE_SETTLE_MS)
            log_throttled(ctx, "task_combat_cleared", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] combat objective appears cleared, refresh next task | idle_ms=%d last_count=%d",
                tonumber(combat_idle_ms) or 0,
                tonumber(state.task_combat_last_count) or 0
            ))
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end

        state.task_reached_unresolved_since = 0
        state.stage = "task_combat_settle"
        hold_navigation(ctx, current_time, "task_combat_settle")
        log_throttled(ctx, "task_combat_settle", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] waiting combat objective settle before refreshing next task | idle_ms=%d last_count=%d",
            tonumber(combat_idle_ms) or 0,
            tonumber(state.task_combat_last_count) or 0
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    clear_task_combat_state()

    local has_task_path_snapshot = type(state.task_path) == "table" and #state.task_path > 0
    local has_nearby_blocking_monster = false
    if task_monsters and tonumber(task_monsters.count) and tonumber(task_monsters.count) > 0 then
        local nearest_monster = task_monsters.nearest or {}
        if tonumber(nearest_monster.distance) ~= nil
            and tonumber(nearest_monster.distance) <= NPC_DIALOGUE_MONSTER_BLOCK_DISTANCE
        then
            has_nearby_blocking_monster = true
        end
    end

    if nearest_npc or has_nearby_blocking_monster then
        state.task_reached_unresolved_since = 0
    else
        if (tonumber(state.task_reached_unresolved_since) or 0) <= 0 then
            state.task_reached_unresolved_since = current_time
        end
        local unresolved_ms = current_time - (tonumber(state.task_reached_unresolved_since) or current_time)
        local spawn_wait_ms = 1800
        if not task_monsters then
            spawn_wait_ms = has_task_path_snapshot and 6500 or 4200
        end
        if unresolved_ms >= spawn_wait_ms then
            state.task_reached_unresolved_since = 0
            local followup_route_action_key = type(objective_cfg) == "table"
                and tostring(objective_cfg.followup_route_action_key or "")
                or ""
            if followup_route_action_key ~= "" then
                local action, action_err = M.activate_route_point_action(
                    ctx,
                    current_time,
                    followup_route_action_key,
                    "task_reached_followup"
                )
                if type(action) == "table" then
                    log_throttled(ctx, "task_reached_followup_route_action", "info", LOG_THROTTLE_MS, string.format(
                        "[Leveling] objective follow-up route action armed at task reached | task=%s objective=%s action=%s mode=%s unresolved_ms=%d",
                        tostring(task_name or state.current_task_name or ""),
                        tostring(type(objective_cfg) == "table" and objective_cfg.key or ""),
                        tostring(followup_route_action_key),
                        tostring(action.mode or ""),
                        tonumber(unresolved_ms) or 0
                    ))
                    if tostring(action.mode or "") == "npc_dialogue_point"
                        and M.maybe_handle_route_point_action_npc_dialogue(ctx, current_time, player_x, player_y, player_z)
                    then
                        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
                        return true
                    end
                    if tostring(action.mode or "") == "lift_transition"
                        and M.maybe_handle_route_point_action_boarding(ctx, current_time, player_x, player_y, player_z)
                    then
                        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
                        return true
                    end
                    if tostring(action.mode or "") == "recorded_route_point"
                        and M.maybe_handle_route_point_action_route(ctx, current_time, player_x, player_y, player_z)
                    then
                        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
                        return true
                    end
                    log_heartbeat(ctx, current_time, player_x, player_y, player_z)
                    return true
                end
                log_throttled(ctx, "task_reached_followup_route_action_failed", "warn", LOG_THROTTLE_MS, string.format(
                    "[Leveling] objective follow-up route action unavailable at task reached | task=%s objective=%s action=%s err=%s",
                    tostring(task_name or state.current_task_name or ""),
                    tostring(type(objective_cfg) == "table" and objective_cfg.key or ""),
                    tostring(followup_route_action_key),
                    tostring(action_err or "")
                ))
            end
            schedule_task_refresh_after_transition(ctx, current_time, "task_reached_no_objective", TASK_BUTTON_SETTLE_MS)
            log_throttled(ctx, "task_reached_no_objective", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] task objective completed without dialogue/object prompt, refresh next task | unresolved_ms=%d monsters=%d has_path=%s",
                    tonumber(unresolved_ms) or 0,
                tonumber(task_monsters and task_monsters.count) or 0,
                has_task_path_snapshot and "true" or "false"
            ))
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end

        if not task_monsters then
            state.stage = "task_spawn_wait"
            hold_navigation(ctx, current_time, "task_spawn_wait")
            log_throttled(ctx, "task_spawn_wait", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] waiting task cutscene/spawn/bridge at target before refreshing | waited=%dms has_path=%s",
                tonumber(unresolved_ms) or 0,
                has_task_path_snapshot and "true" or "false"
            ))
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end
    end

    if npc_err then
        log_throttled(ctx, "npc_missing", "info", LOG_THROTTLE_MS,
            "[Leveling] reached task target but no nearby NPC confirmed yet: " .. tostring(npc_err))
    end
    if monster_err then
        log_throttled(ctx, "task_objective_unknown", "info", LOG_THROTTLE_MS,
            "[Leveling] task objective unresolved | no NPC or monster near target, may be device/protect objective: " .. tostring(monster_err))
    end

    if current_time >= (tonumber(state.next_task_button_soft_refresh_at) or 0) then
        schedule_task_refresh_after_transition(ctx, current_time, "task_reached_next_task", POST_DIALOGUE_SETTLE_MS)
        log_throttled(ctx, "task_target_hard_refresh_at_target", "info", LOG_THROTTLE_MS,
            "[Leveling] unresolved task objective scheduled next-task refresh.")
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    log_throttled(ctx, "task_reached", "info", LOG_THROTTLE_MS, string.format(
        "[Leveling] near task target | distance=%.2f source=%s",
        goal_distance,
        target_source
    ))
    hold_navigation(ctx, current_time, "task_reached")
    log_heartbeat(ctx, current_time, player_x, player_y, player_z)
    return true
end

local function map_transition_state_key(map_name, transition)
    return table.concat({
        tostring(normalize_map_name(map_name) or ""),
        tostring(type(transition) == "table" and (transition.key or transition.label) or "")
    }, "|")
end

function M.route_point_action_state_key(action)
    return table.concat({
        tostring(normalize_map_name(M.current_task_log_name()) or ""),
        tostring(type(action) == "table" and (action.key or action.label) or "")
    }, "|")
end

function M.find_route_point_action_by_key(action_key)
    if type(M.ROUTE_POINT_ACTIONS) ~= "table" or action_key == nil or action_key == "" then
        return nil
    end
    local normalized_key = tostring(action_key)
    for _, action in ipairs(M.ROUTE_POINT_ACTIONS) do
        local candidate = tostring(type(action) == "table" and (action.key or action.label) or "")
        if candidate ~= "" and candidate == normalized_key then
            return action
        end
    end
    return nil
end

function M.route_point_action_matches_task(action)
    if type(action) ~= "table" then
        return false
    end

    return M.matches_task_constraints(action)
end

function M.route_point_action_has_map_constraints(action)
    return type(action) == "table"
        and (type(action.map_names) == "table" or type(action.map_patterns) == "table")
end

function M.route_point_action_matches_map_name(action, map_name)
    if not M.route_point_action_has_map_constraints(action) then
        return true, normalize_map_name(map_name)
    end

    local map_key = normalize_map_name(map_name)
    if map_key == nil then
        return false, nil
    end

    local function contains_exact(list)
        if type(list) ~= "table" then
            return false
        end
        for _, item in ipairs(list) do
            local item_key = normalize_map_name(item)
            if item_key ~= nil and item_key == map_key then
                return true
            end
        end
        return false
    end

    local function contains_pattern(list)
        if type(list) ~= "table" then
            return false
        end
        for _, item in ipairs(list) do
            local item_key = normalize_map_name(item)
            if item_key ~= nil and map_key:find(item_key, 1, true) then
                return true
            end
        end
        return false
    end

    return contains_exact(action.map_names) or contains_pattern(action.map_patterns), map_key
end

function M.refresh_route_point_action_map_name(ctx, current_time, force)
    current_time = tonumber(current_time) or now_ms(ctx)
    local next_probe_at = tonumber(state.next_route_point_action_map_probe_at) or 0
    if force ~= true and current_time < next_probe_at then
        return state.cached_route_point_action_map_name, state.cached_route_point_action_map_error
    end

    state.next_route_point_action_map_probe_at = current_time + 1200
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table"
        or type(nav_mod.enum_ui) ~= "function"
        or type(nav_mod.get_current_map_name) ~= "function"
    then
        state.cached_route_point_action_map_name = nil
        state.cached_route_point_action_map_error = "map ui API is unavailable."
        return nil, state.cached_route_point_action_map_error
    end

    local ui, ui_err = nav_mod.enum_ui()
    if type(ui) ~= "table" then
        state.cached_route_point_action_map_name = nil
        state.cached_route_point_action_map_error = tostring(ui_err or "enum_ui failed.")
        return nil, state.cached_route_point_action_map_error
    end

    local map_name, map_err = nav_mod.get_current_map_name(ui)
    map_name = trim(map_name)
    if map_name == "" then
        state.cached_route_point_action_map_name = nil
        state.cached_route_point_action_map_error = tostring(map_err or "current map not found.")
        return nil, state.cached_route_point_action_map_error
    end

    state.cached_route_point_action_map_name = map_name
    state.cached_route_point_action_map_error = nil
    return map_name, nil
end

function M.route_point_action_matches_current_map(ctx, action, current_time)
    if not M.route_point_action_has_map_constraints(action) then
        return true, normalize_map_name(state.current_map_name or state.cached_route_point_action_map_name), nil
    end

    local map_name = state.current_map_name or state.cached_route_point_action_map_name
    local ok, normalized_map = M.route_point_action_matches_map_name(action, map_name)
    if ok then
        return true, normalized_map, nil
    end

    map_name = select(1, M.refresh_route_point_action_map_name(ctx, current_time, true))
    ok, normalized_map = M.route_point_action_matches_map_name(action, map_name)
    if ok then
        return true, normalized_map, nil
    end

    return false, normalized_map, state.cached_route_point_action_map_error
end

function M.route_point_action_destination_matches(action, player_x, player_y)
    if type(action) ~= "table" or action.require_destination_match ~= true then
        return true, nil, nil, nil
    end

    local trigger = type(action.trigger) == "table" and action.trigger or nil
    local trigger_x = tonumber(trigger and trigger.x)
    local trigger_y = tonumber(trigger and trigger.y)
    if trigger_x == nil or trigger_y == nil then
        return false, nil, "route point trigger unavailable", nil
    end

    local destination = build_task_destination_point(player_x, player_y)
    if type(destination) ~= "table" or tonumber(destination.x) == nil or tonumber(destination.y) == nil then
        return false, nil, "task destination unavailable", nil
    end

    local match_radius = math.max(
        80,
        tonumber(action.destination_match_radius) or tonumber(trigger.radius) or 220
    )
    local destination_distance = distance_2d(
        tonumber(destination.x) or trigger_x,
        tonumber(destination.y) or trigger_y,
        trigger_x,
        trigger_y
    )
    return destination_distance <= match_radius, destination_distance, nil, destination
end

function M.pick_route_point_action_level_candidate(ui)
    if type(ui) ~= "table" or type(ui.texts) ~= "table" then
        return nil
    end

    local best = nil
    for _, item in ipairs(ui.texts or {}) do
        local text = trim(type(item) == "table" and item.text or "")
        if text ~= "" then
            local normalized = text:lower()
            local score = 0
            if text:match("\u{7B49}\u{7EA7}%s*%d+") then
                score = score + 120
            end
            if text:match("\u{7B49}\u{7EA7}%s*%d+%s*%(%d+%%%)") then
                score = score + 80
            end
            if normalized:match("lv%s*%d+") or normalized:match("level%s*%d+") then
                score = score + 60
            end
            if text:match("%d+%%") then
                score = score + 18
            end
            if text:find("\u{63A8}\u{8350}", 1, true) then
                score = score - 120
            end
            if text:find("\u{9700}\u{6C42}\u{7B49}\u{7EA7}", 1, true) then
                score = score - 160
            end
            if text:find("\u{602A}\u{7269}\u{7B49}\u{7EA7}", 1, true) then
                score = score - 160
            end
            if text:find("\u{5173}\u{5361}\u{7B49}\u{7EA7}", 1, true) then
                score = score - 160
            end

            local level_value = text:match("\u{7B49}\u{7EA7}%s*(%d+)")
                or normalized:match("lv%s*(%d+)")
                or normalized:match("level%s*(%d+)")
            if level_value ~= nil and score > 0 then
                local candidate = {
                    text = text,
                    level = tonumber(level_value),
                    progress = tonumber(text:match("%((%d+)%%%s*%)") or text:match("(%d+)%%%s*$") or ""),
                    score = score,
                    x = tonumber(type(item) == "table" and item.x),
                    y = tonumber(type(item) == "table" and item.y),
                    name = tostring(type(item) == "table" and item.name or "")
                }
                if best == nil then
                    best = candidate
                else
                    local best_score = tonumber(best.score) or 0
                    local best_y = tonumber(best.y) or math.huge
                    local candidate_y = tonumber(candidate.y) or math.huge
                    if score > best_score or (score == best_score and candidate_y > best_y) then
                        best = candidate
                    end
                end
            end
        end
    end
    return best
end

function M.refresh_route_point_action_player_level(ctx, current_time, force)
    current_time = tonumber(current_time) or now_ms(ctx)
    local next_probe_at = tonumber(state.next_route_point_action_level_probe_at) or 0
    if force ~= true and current_time < next_probe_at then
        return state.cached_route_point_action_player_level,
            state.cached_route_point_action_player_level_progress,
            state.cached_route_point_action_player_level_text,
            state.cached_route_point_action_player_level_error
    end

    state.next_route_point_action_level_probe_at = current_time + 1200
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.enum_ui) ~= "function" then
        state.cached_route_point_action_player_level_error = "nav.enum_ui is unavailable."
        return state.cached_route_point_action_player_level,
            state.cached_route_point_action_player_level_progress,
            state.cached_route_point_action_player_level_text,
            state.cached_route_point_action_player_level_error
    end

    local ui, ui_err = nav_mod.enum_ui()
    if type(ui) ~= "table" then
        state.cached_route_point_action_player_level_error = tostring(ui_err or "enum_ui failed.")
        return state.cached_route_point_action_player_level,
            state.cached_route_point_action_player_level_progress,
            state.cached_route_point_action_player_level_text,
            state.cached_route_point_action_player_level_error
    end

    local best = M.pick_route_point_action_level_candidate(ui)
    if type(best) ~= "table" or type(best.level) ~= "number" then
        state.cached_route_point_action_player_level_error = "player level text not found."
        return state.cached_route_point_action_player_level,
            state.cached_route_point_action_player_level_progress,
            state.cached_route_point_action_player_level_text,
            state.cached_route_point_action_player_level_error
    end

    state.cached_route_point_action_player_level = math.floor(best.level)
    state.cached_route_point_action_player_level_progress = best.progress
    state.cached_route_point_action_player_level_text = best.text
    state.cached_route_point_action_player_level_error = nil
    return state.cached_route_point_action_player_level,
        state.cached_route_point_action_player_level_progress,
        state.cached_route_point_action_player_level_text,
        nil
end

function M.route_point_action_skip_reason(ctx, action, current_time)
    if type(action) ~= "table" then
        return nil
    end

    local treasure_key = trim(action.skip_when_treasure_completed_key or "")
    local treasures = type(state.treasure_persisted) == "table"
        and type(state.treasure_persisted.treasures) == "table"
        and state.treasure_persisted.treasures
        or nil
    local treasure_record = type(treasures) == "table" and treasures[treasure_key] or nil
    if treasure_key ~= "" and type(treasure_record) == "table" and treasure_record.completed == true then
        return "treasure_completed", treasure_key
    end

    local min_level = tonumber(action.skip_when_player_level_at_least)
    if min_level ~= nil and min_level > 0 then
        local level, progress, text, err = M.refresh_route_point_action_player_level(ctx, current_time, false)
        if type(level) == "number" and level >= min_level then
            return "player_level_reached", string.format(
                "level=%d progress=%s target=%d text=%s",
                level,
                progress ~= nil and tostring(progress) or "",
                math.floor(min_level),
                tostring(text or "")
            )
        end
        if err ~= nil then
            return nil, err
        end
    end

    return nil
end

function M.find_npc_near_point(ctx, point_x, point_y, search_radius, player_x, player_y)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.enum_npcs) ~= "function" then
        return nil, "nav.enum_npcs is unavailable."
    end

    local items, enum_err = nav_mod.enum_npcs()
    if type(items) ~= "table" then
        return nil, enum_err or "EnumNPC failed."
    end

    local anchor_x = tonumber(point_x)
    local anchor_y = tonumber(point_y)
    if anchor_x == nil or anchor_y == nil then
        return nil, "NPC anchor point is unavailable."
    end

    local max_distance = math.max(120, tonumber(search_radius) or 420)
    local nearest = nil
    local best_anchor_distance = nil

    for _, item in ipairs(items) do
        local x, y, z = extract_position_from_item(ctx, item)
        if x ~= nil and y ~= nil then
            local anchor_distance = distance_2d(anchor_x, anchor_y, x, y)
            if anchor_distance <= max_distance
                and (best_anchor_distance == nil or anchor_distance < best_anchor_distance)
            then
                nearest = {
                    item = item,
                    x = x,
                    y = y,
                    z = z,
                    label = npc_label(item),
                    point_distance = anchor_distance,
                    distance = type(player_x) == "number" and type(player_y) == "number"
                        and distance_2d(player_x, player_y, x, y)
                        or anchor_distance
                }
                best_anchor_distance = anchor_distance
            end
        end
    end

    if nearest then
        return nearest, nil
    end

    return nil, string.format("No NPC near point %.2f, %.2f within %.2f", anchor_x, anchor_y, max_distance)
end

function M.arm_route_point_action_board(ctx, action, state_key, current_time, source)
    local board = type(action) == "table" and action.board or nil
    if type(board) ~= "table" then
        return false
    end

    state.route_point_action_active_key = tostring(action.key or action.label or "")
    state.route_point_action_active_state_key = tostring(state_key or "")
    state.route_point_action_board_entered_at = 0
    state.route_point_action_board_next_interact_at = tonumber(current_time) or now_ms(ctx)
    state.route_point_action_board_deadline_at = (tonumber(current_time) or now_ms(ctx))
        + math.max(6000, tonumber(board.timeout_ms) or 18000)

    logger(ctx).info(string.format(
        "[Leveling] route point action board armed | task=%s key=%s source=%s center=%.2f, %.2f, %.2f radius=%.2f timeout=%dms",
        tostring(M.current_task_log_name() or state.current_task_name or ""),
        tostring(action.key or action.label or ""),
        tostring(source or "trigger"),
        tonumber(board.x) or 0,
        tonumber(board.y) or 0,
        tonumber(board.z) or 0,
        math.max(80, tonumber(board.radius) or 180),
        math.max(6000, tonumber(board.timeout_ms) or 18000)
    ))

    return true
end

function M.arm_route_point_action_npc_dialogue(ctx, action, state_key, current_time, source)
    local dialogue = type(action) == "table" and action.dialogue or nil
    if type(dialogue) ~= "table" then
        return false
    end

    state.route_point_action_dialogue_active_key = tostring(action.key or action.label or "")
    state.route_point_action_dialogue_active_state_key = tostring(state_key or "")
    state.route_point_action_dialogue_entered_at = 0
    state.route_point_action_dialogue_next_interact_at = tonumber(current_time) or now_ms(ctx)
    state.route_point_action_dialogue_deadline_at = (tonumber(current_time) or now_ms(ctx))
        + math.max(6000, tonumber(dialogue.timeout_ms) or 18000)

    logger(ctx).info(string.format(
        "[Leveling] route point action npc dialogue armed | task=%s key=%s source=%s point=%.2f, %.2f, %.2f radius=%.2f timeout=%dms",
        tostring(M.current_task_log_name() or state.current_task_name or ""),
        tostring(action.key or action.label or ""),
        tostring(source or "trigger"),
        tonumber(dialogue.x) or 0,
        tonumber(dialogue.y) or 0,
        tonumber(dialogue.z) or 0,
        math.max(80, tonumber(dialogue.radius) or 220),
        math.max(6000, tonumber(dialogue.timeout_ms) or 18000)
    ))

    return true
end

function M.arm_route_point_action_objective(ctx, action, state_key, current_time, source)
    local trigger = type(action) == "table" and action.trigger or nil
    local step = type(action) == "table" and action.step or nil
    if type(trigger) ~= "table" or type(step) ~= "table" then
        return false
    end

    state.route_point_action_objective_active_key = tostring(action.key or action.label or "")
    state.route_point_action_objective_active_state_key = tostring(state_key or "")
    state.route_point_action_objective_entered_at = tonumber(current_time) or now_ms(ctx)
    state.route_point_action_objective_next_probe_at = tonumber(current_time) or now_ms(ctx)
    state.route_point_action_objective_deadline_at = (tonumber(current_time) or now_ms(ctx))
        + math.max(6000, tonumber(action.timeout_ms) or 18000)

    logger(ctx).info(string.format(
        "[Leveling] route point action objective armed | task=%s key=%s source=%s point=%.2f, %.2f, %.2f radius=%.2f interact_radius=%.2f timeout=%dms",
        tostring(M.current_task_log_name() or state.current_task_name or ""),
        tostring(action.key or action.label or ""),
        tostring(source or "trigger"),
        tonumber(trigger.x) or 0,
        tonumber(trigger.y) or 0,
        tonumber(trigger.z) or 0,
        math.max(80, tonumber(trigger.radius) or 220),
        math.max(40, tonumber(action.interact_radius) or tonumber(step.hint_max_distance) or 120),
        math.max(6000, tonumber(action.timeout_ms) or 18000)
    ))

    return true
end

function M.arm_task_entry_action_after_route_point(ctx, current_time, action)
    local entry_action, task_name = M.current_task_entry_action_config()
    if type(entry_action) ~= "table" or tostring(entry_action.mode or "") ~= "world_map_send" then
        return false, "current task has no world_map_send entry action"
    end

    current_time = tonumber(current_time) or now_ms(ctx)
    local map_open_wait_ms = math.max(200, tonumber(entry_action.map_open_wait_ms) or 900)
    local timeout_ms = math.max(5000, tonumber(entry_action.timeout_ms) or 12000)

    clear_task_target_state()
    clear_runtime_objective_caches()
    M.clear_task_entry_action_state()

    state.require_task_button_refresh = false
    state.task_update_wait_until = 0
    state.last_task_button_click_at = current_time
    state.task_entry_action_button_click_at = current_time
    state.task_entry_action_center_clicked_at = 0
    state.task_entry_action_next_center_click_at = current_time + map_open_wait_ms
    state.task_entry_action_pre_clicked_at = 0
    state.task_entry_action_send_clicked_at = 0
    state.task_entry_action_locked_cfg = entry_action
    state.task_entry_action_locked_task_name = task_name
    state.task_entry_action_locked_key = tostring(entry_action.key or "")
    state.task_path_wait_until = math.max(
        tonumber(state.task_path_wait_until) or 0,
        current_time + map_open_wait_ms + 1000
    )
    state.next_task_button_click_at = math.max(
        tonumber(state.next_task_button_click_at) or 0,
        current_time + timeout_ms
    )
    state.next_task_refresh_at = math.max(
        tonumber(state.next_task_refresh_at) or 0,
        current_time + map_open_wait_ms
    )
    state.pause_combat_until = math.max(
        tonumber(state.pause_combat_until) or 0,
        current_time + POST_UI_PAUSE_MS
    )
    clear_pending_interaction()

    logger(ctx).info(string.format(
        "[Leveling] route point action armed task entry action | task=%s key=%s entry_key=%s wait=%dms",
        tostring(task_name or state.current_task_name or ""),
        tostring(type(action) == "table" and (action.key or action.label) or ""),
        tostring(entry_action.key or ""),
        tonumber(map_open_wait_ms) or 0
    ))
    return true, nil
end

function M.arm_task_entry_action_after_task_objective(ctx, current_time, objective_button)
    local entry_action, task_name = M.current_task_entry_action_config()
    if type(entry_action) ~= "table" or tostring(entry_action.mode or "") ~= "world_map_send" then
        return false, "current task has no world_map_send entry action"
    end

    current_time = tonumber(current_time) or now_ms(ctx)
    local map_open_wait_ms = math.max(200, tonumber(entry_action.map_open_wait_ms) or 900)
    local timeout_ms = math.max(5000, tonumber(entry_action.timeout_ms) or 12000)
    local step = type(objective_button) == "table" and objective_button.step or nil

    clear_task_target_state()
    clear_runtime_objective_caches()
    M.clear_task_entry_action_state()

    state.require_task_button_refresh = false
    state.require_task_button_refresh_reason = nil
    state.task_update_wait_until = 0
    state.last_task_button_click_at = current_time
    state.task_entry_action_button_click_at = current_time
    state.task_entry_action_center_clicked_at = 0
    state.task_entry_action_next_center_click_at = current_time + map_open_wait_ms
    state.task_entry_action_pre_clicked_at = 0
    state.task_entry_action_send_clicked_at = 0
    state.task_entry_action_locked_cfg = entry_action
    state.task_entry_action_locked_task_name = task_name
    state.task_entry_action_locked_key = tostring(entry_action.key or "")
    state.task_path_wait_until = math.max(
        tonumber(state.task_path_wait_until) or 0,
        current_time + map_open_wait_ms + 1000
    )
    state.next_task_button_click_at = math.max(
        tonumber(state.next_task_button_click_at) or 0,
        current_time + timeout_ms
    )
    state.next_task_refresh_at = math.max(
        tonumber(state.next_task_refresh_at) or 0,
        current_time + map_open_wait_ms
    )
    state.pause_combat_until = math.max(
        tonumber(state.pause_combat_until) or 0,
        current_time + POST_UI_PAUSE_MS
    )
    clear_pending_interaction()

    logger(ctx).info(string.format(
        "[Leveling] task objective button -> task entry action armed | task=%s label=%s entry_key=%s wait=%dms",
        tostring(task_name or state.current_task_name or ""),
        tostring(type(step) == "table" and (step.label or step.key) or ""),
        tostring(entry_action.key or ""),
        tonumber(map_open_wait_ms) or 0
    ))
    return true, nil
end

function M.maybe_handle_route_point_action_route_wait(ctx, current_time, player_x, player_y, player_z)
    current_time = tonumber(current_time) or now_ms(ctx)
    local wait_until = tonumber(state.route_point_action_route_wait_reacquire_until) or 0
    if wait_until <= 0 then
        return false
    end

    if current_time >= (tonumber(state.next_task_name_probe_at) or 0) then
        local hint_x, hint_y = resolve_main_task_button_hint(ctx)
        if hint_x ~= nil and hint_y ~= nil then
            M.refresh_current_task_name(ctx, current_time, nil, hint_x, hint_y)
        end
        state.next_task_name_probe_at = current_time + 800
    end

    local locked_task_name = normalize_map_name(state.route_point_action_route_wait_task_name)
    local locked_task_detail = normalize_map_name(state.route_point_action_route_wait_task_detail)
    local current_task_name = normalize_map_name(M.current_task_log_name())
    local current_task_detail = normalize_map_name(M.current_task_log_detail())
    local current_task_source = tostring(state.current_task_name_source or "")
    local current_detail_source = tostring(state.current_task_detail_source or "")
    local task_name_changed = locked_task_name ~= nil
        and current_task_name ~= nil
        and current_task_name ~= locked_task_name
    local task_detail_changed = locked_task_detail ~= nil
        and current_task_detail ~= nil
        and current_task_detail ~= locked_task_detail
    local soft_probe_change = (current_task_source == "nearby_text" and task_name_changed)
        or (current_detail_source == "nearby_text" and task_detail_changed)

    if (task_name_changed or task_detail_changed) and not soft_probe_change then
        logger(ctx).info(string.format(
            "[Leveling] route point action wait released by task refresh | key=%s old_task=%s old_detail=%s new_task=%s new_detail=%s",
            tostring(state.route_point_action_route_wait_key or ""),
            tostring(state.route_point_action_route_wait_task_name or ""),
            tostring(state.route_point_action_route_wait_task_detail or ""),
            tostring(M.current_task_log_name() or ""),
            tostring(M.current_task_log_detail() or "")
        ))
        M.clear_route_point_action_route_wait_state()
        state.next_task_button_click_at = math.min(tonumber(state.next_task_button_click_at) or current_time, current_time)
        state.next_task_refresh_at = math.min(tonumber(state.next_task_refresh_at) or current_time, current_time)
        return false
    end

    if current_time >= wait_until then
        logger(ctx).info(string.format(
            "[Leveling] route point action wait expired, resume normal task reacquire | key=%s",
            tostring(state.route_point_action_route_wait_key or "")
        ))
        M.clear_route_point_action_route_wait_state()
        state.next_task_button_click_at = math.min(tonumber(state.next_task_button_click_at) or current_time, current_time)
        state.next_task_refresh_at = math.min(tonumber(state.next_task_refresh_at) or current_time, current_time)
        return false
    end

    state.stage = "route_point_action_route_wait_task_refresh"
    release_async_combat_inputs(ctx, current_time, true)
    hold_navigation(ctx, current_time, "route_point_action_route_wait_task_refresh")
    log_throttled(
        ctx,
        "route_point_action_route_wait_task_refresh_" .. tostring(state.route_point_action_route_wait_key or ""),
        "info",
        LOG_THROTTLE_MS,
        string.format(
            "[Leveling] route point action waiting task refresh | task=%s detail=%s key=%s remaining_ms=%d",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(M.current_task_log_detail() or state.current_task_detail or ""),
            tostring(state.route_point_action_route_wait_key or ""),
            math.max(0, wait_until - current_time)
        )
    )
    log_heartbeat(ctx, current_time, player_x, player_y, player_z)
    return true
end

function M.arm_route_point_action_route(ctx, action, state_key, current_time, source)
    local waypoints = type(action) == "table" and action.waypoints or nil
    if type(waypoints) ~= "table" or #waypoints <= 0 then
        return false
    end

    local first_index = nil
    local first_point = nil
    for index, point in ipairs(waypoints) do
        local point_x = tonumber(type(point) == "table" and point.x)
        local point_y = tonumber(type(point) == "table" and point.y)
        if point_x ~= nil and point_y ~= nil then
            first_index = index
            first_point = point
            break
        end
    end
    if first_index == nil or type(first_point) ~= "table" then
        return false
    end

    local timeout_ms = math.max(12000, tonumber(action.timeout_ms) or (#waypoints * 8000))
    state.route_point_action_route_active_key = tostring(action.key or action.label or "")
    state.route_point_action_route_active_state_key = tostring(state_key or "")
    state.route_point_action_route_started_at = tonumber(current_time) or now_ms(ctx)
    state.route_point_action_route_deadline_at = (tonumber(current_time) or now_ms(ctx)) + timeout_ms
    state.route_point_action_route_index = first_index
    state.route_point_action_route_next_retry_at = tonumber(current_time) or now_ms(ctx)

    logger(ctx).info(string.format(
        "[Leveling] route point action recorded route armed | task=%s key=%s source=%s points=%d start_index=%d first=%.2f, %.2f, %.2f timeout=%dms",
        tostring(M.current_task_log_name() or state.current_task_name or ""),
        tostring(action.key or action.label or ""),
        tostring(source or "trigger"),
        #waypoints,
        tonumber(first_index) or 1,
        tonumber(first_point.x) or 0,
        tonumber(first_point.y) or 0,
        tonumber(first_point.z) or 0,
        timeout_ms
    ))

    return true
end

function M.activate_route_point_action(ctx, current_time, action_key, source)
    local action = M.find_route_point_action_by_key(action_key)
    if type(action) ~= "table" then
        return nil, "route point action not found"
    end

    local mode = tostring(action.mode or "")
    local state_key = M.route_point_action_state_key(action)
    local last_triggered_at = tonumber(state.map_transition_triggered[state_key]) or 0
    local retry_ms = math.max(1000, tonumber(action.retry_ms) or 3000)
    if last_triggered_at > 0 and current_time - last_triggered_at < retry_ms then
        return nil, string.format("retry_guard remaining=%dms", math.max(0, retry_ms - (current_time - last_triggered_at)))
    end

    if mode == "npc_dialogue_point" then
        M.clear_route_point_action_board_state()
        M.clear_route_point_action_objective_state()
        M.clear_route_point_action_route_state()
        if tostring(state.route_point_action_dialogue_active_key or "") ~= tostring(action.key or action.label or "") then
            M.clear_route_point_action_npc_dialogue_state()
        end
        if M.arm_route_point_action_npc_dialogue(ctx, action, state_key, current_time, source) then
            return action, nil
        end
        return nil, "npc dialogue arm failed"
    end

    if mode == "lift_transition" then
        M.clear_route_point_action_npc_dialogue_state()
        M.clear_route_point_action_objective_state()
        M.clear_route_point_action_route_state()
        if tostring(state.route_point_action_active_key or "") ~= tostring(action.key or action.label or "") then
            M.clear_route_point_action_board_state()
        end
        if M.arm_route_point_action_board(ctx, action, state_key, current_time, source) then
            return action, nil
        end
        return nil, "lift board arm failed"
    end

    if mode == "objective_button_flow_point" then
        M.clear_route_point_action_board_state()
        M.clear_route_point_action_npc_dialogue_state()
        M.clear_route_point_action_route_state()
        if tostring(state.route_point_action_objective_active_key or "") ~= tostring(action.key or action.label or "") then
            M.clear_route_point_action_objective_state()
        end
        if M.arm_route_point_action_objective(ctx, action, state_key, current_time, source) then
            return action, nil
        end
        return nil, "objective button flow arm failed"
    end

    if mode == "recorded_route_point" then
        M.clear_route_point_action_board_state()
        M.clear_route_point_action_npc_dialogue_state()
        M.clear_route_point_action_objective_state()
        if tostring(state.route_point_action_route_active_key or "") ~= tostring(action.key or action.label or "") then
            M.clear_route_point_action_route_state()
        end
        if M.arm_route_point_action_route(ctx, action, state_key, current_time, source) then
            return action, nil
        end
        return nil, "recorded route arm failed"
    end

    return nil, "unsupported route point action mode"
end

function M.maybe_handle_route_point_action_boarding(ctx, current_time, player_x, player_y, player_z)
    local active_key = tostring(state.route_point_action_active_key or "")
    if active_key == "" and type(M.ROUTE_POINT_ACTIONS) == "table" then
        if M.is_task_combat_or_post_loot_active() then
            return false
        end
        if state.require_task_button_refresh == true
            or current_time < (tonumber(state.task_update_wait_until) or 0)
            or (tonumber(state.dialogue_escape_due_at) or 0) > 0
        then
            return false
        end

        for _, action in ipairs(M.ROUTE_POINT_ACTIONS) do
            local board = type(action) == "table" and action.board or nil
            local task_match = M.route_point_action_matches_task(action)
            if type(board) == "table"
                and board.allow_direct_entry == true
                and (task_match or board.allow_direct_entry_without_task_match == true)
            then
                local state_key = M.route_point_action_state_key(action)
                local board_guard_key = tostring(state.route_point_action_board_guard_key or "")
                local board_guard_until = tonumber(state.route_point_action_board_guard_until) or 0
                if board_guard_key ~= ""
                    and board_guard_key == state_key
                    and current_time < board_guard_until
                then
                    log_throttled(
                        ctx,
                        "route_point_action_board_guard_" .. tostring(state_key),
                        "info",
                        LOG_THROTTLE_MS,
                        string.format(
                            "[Leveling] route point action board direct entry suppressed | task=%s key=%s remaining_ms=%d",
                            tostring(M.current_task_log_name() or state.current_task_name or ""),
                            tostring(action.key or action.label or ""),
                            math.max(0, board_guard_until - current_time)
                        )
                    )
                    goto continue_route_point_board_direct_entry
                end

                local last_triggered_at = tonumber(state.map_transition_triggered[state_key]) or 0
                local retry_ms = math.max(
                    1000,
                    tonumber(board.direct_entry_retry_ms) or tonumber(action.retry_ms) or 3000
                )
                if last_triggered_at > 0 and current_time - last_triggered_at < retry_ms then
                    goto continue_route_point_board_direct_entry
                end

                local board_x = tonumber(board.x)
                local board_y = tonumber(board.y)
                local board_z = tonumber(board.z)
                if board_x ~= nil and board_y ~= nil then
                    local direct_entry_radius = math.max(
                        math.max(120, tonumber(board.radius) or 180),
                        tonumber(board.direct_entry_radius) or 0
                    )
                    local direct_entry_z_tolerance = math.max(
                        0,
                        tonumber(board.direct_entry_z_tolerance) or tonumber(board.z_tolerance) or 260
                    )
                    local direct_entry_distance = distance_2d(player_x, player_y, board_x, board_y)
                    local direct_entry_z_gap = board_z ~= nil and type(player_z) == "number"
                        and math.abs((tonumber(player_z) or 0) - board_z)
                        or 0
                    if direct_entry_distance <= direct_entry_radius and direct_entry_z_gap <= direct_entry_z_tolerance then
                        local state_key = M.route_point_action_state_key(action)
                        M.arm_route_point_action_board(ctx, action, state_key, current_time, "direct_entry")
                        logger(ctx).info(string.format(
                            "[Leveling] route point action board direct entry | task=%s key=%s task_match=%s distance=%.2f z_gap=%.2f radius=%.2f",
                            tostring(M.current_task_log_name() or state.current_task_name or ""),
                            tostring(action.key or action.label or ""),
                            tostring(task_match == true),
                            tonumber(direct_entry_distance) or 0,
                            tonumber(direct_entry_z_gap) or 0,
                            tonumber(direct_entry_radius) or 0
                        ))
                        active_key = tostring(state.route_point_action_active_key or "")
                        break
                    end
                end
            end
            ::continue_route_point_board_direct_entry::
        end
    end
    if active_key == "" then
        return false
    end

    local action = M.find_route_point_action_by_key(active_key)
    local board = type(action) == "table" and action.board or nil
    if type(action) ~= "table" or type(board) ~= "table" then
        M.clear_route_point_action_board_state()
        return false
    end

    local board_x = tonumber(board.x)
    local board_y = tonumber(board.y)
    local board_z = tonumber(board.z)
    if board_x == nil or board_y == nil then
        M.clear_route_point_action_board_state()
        return false
    end

    local deadline_at = tonumber(state.route_point_action_board_deadline_at) or 0
    if deadline_at > 0 and current_time > deadline_at then
        log_throttled(
            ctx,
            "route_point_action_board_timeout_" .. tostring(active_key),
            "warn",
            LOG_THROTTLE_MS,
            string.format(
                "[Leveling] route point action board timeout | task=%s key=%s center=%.2f, %.2f, %.2f",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(active_key),
                tonumber(board_x) or 0,
                tonumber(board_y) or 0,
                tonumber(board_z) or 0
            )
        )
        state.route_point_action_attempted[tostring(state.route_point_action_active_state_key or active_key)] = nil
        M.clear_route_point_action_board_state()
        return false
    end

    local board_radius = math.max(80, tonumber(board.radius) or 180)
    local board_interact_radius = math.max(50, tonumber(board.interact_radius) or math.min(board_radius, 90))
    local board_move_interval_ms = math.max(120, tonumber(board.move_interval_ms) or 250)
    local board_z_tolerance = math.max(0, tonumber(board.z_tolerance) or 260)
    local board_distance = distance_2d(player_x, player_y, board_x, board_y)
    local board_z_gap = board_z ~= nil and type(player_z) == "number"
        and math.abs((tonumber(player_z) or 0) - board_z)
        or 0

    if board_distance > board_interact_radius or board_z_gap > board_z_tolerance then
        state.stage = "route_point_action_board_move"
        release_async_combat_inputs(ctx, current_time, true)
        issue_move(ctx, current_time, {
            x = board_x,
            y = board_y,
            z = board_z,
            source = "route_point_board",
            path_index = 0,
            move_interval_ms = board_move_interval_ms
        })
        log_throttled(
            ctx,
            "route_point_action_board_move_" .. tostring(active_key),
            "info",
            900,
            string.format(
                "[Leveling] route point action moving to board center | task=%s key=%s distance=%.2f interact_radius=%.2f z_gap=%.2f center=%.2f, %.2f, %.2f",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(active_key),
                tonumber(board_distance) or 0,
                tonumber(board_interact_radius) or 0,
                tonumber(board_z_gap) or 0,
                tonumber(board_x) or 0,
                tonumber(board_y) or 0,
                tonumber(board_z) or 0
            )
        )
        return true
    end

    state.stage = "route_point_action_board_interact"
    release_async_combat_inputs(ctx, current_time, true)
    hold_navigation(ctx, current_time, "route_point_action_board_interact")

    if (tonumber(state.route_point_action_board_entered_at) or 0) <= 0 then
        state.route_point_action_board_entered_at = current_time
    end

    local center_settle_ms = math.max(0, tonumber(board.center_settle_ms) or 700)
    local entered_at = tonumber(state.route_point_action_board_entered_at) or current_time
    if current_time - entered_at < center_settle_ms then
        log_throttled(
            ctx,
            "route_point_action_board_settle_" .. tostring(active_key),
            "info",
            LOG_THROTTLE_MS,
            string.format(
                "[Leveling] route point action waiting board settle | task=%s key=%s settle_left=%dms",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(active_key),
                math.max(0, center_settle_ms - (current_time - entered_at))
            )
        )
        return true
    end

    local next_interact_at = tonumber(state.route_point_action_board_next_interact_at) or 0
    if current_time < next_interact_at then
        return true
    end

    local ok, err = press_keyboard_hotkey(ctx, current_time, VK_D, "leveling route point board")
    state.route_point_action_board_next_interact_at = current_time + math.max(1000, tonumber(board.interact_retry_ms) or 1800)
    if not ok then
        log_throttled(
            ctx,
            "route_point_action_board_interact_failed_" .. tostring(active_key),
            "warn",
            LOG_THROTTLE_MS,
            "[Leveling] route point action board interact failed: " .. tostring(err or "")
        )
        return true
    end

    local completed_state_key = tostring(state.route_point_action_active_state_key or active_key)
    local settle_ms = math.max(TASK_BUTTON_SETTLE_MS, tonumber(board.settle_ms) or 4500)
    state.map_transition_triggered[completed_state_key] = current_time
    logger(ctx).info(string.format(
        "[Leveling] route point action board interact | task=%s key=%s distance=%.2f z_gap=%.2f wait=%dms",
        tostring(M.current_task_log_name() or state.current_task_name or ""),
        tostring(active_key),
        tonumber(board_distance) or 0,
        tonumber(board_z_gap) or 0,
        tonumber(settle_ms) or 0
    ))

    schedule_task_refresh_after_transition(
        ctx,
        current_time,
        "route_point_action_board_" .. tostring(active_key),
        settle_ms,
        (action.force_task_call_after_transition == true or board.force_task_call_after_transition == true) and {
            force_task_call = true,
            task_pos_reject_extra_ms = tonumber(action.task_pos_reject_extra_ms)
                or tonumber(board.task_pos_reject_extra_ms)
                or 2500
        } or nil
    )

    local post_transition_guard_ms = math.max(
        3000,
        tonumber(board.post_transition_guard_ms) or 0,
        settle_ms + 5000
    )
    state.route_point_action_board_guard_key = completed_state_key
    state.route_point_action_board_guard_until = math.max(
        tonumber(state.route_point_action_board_guard_until) or 0,
        current_time + post_transition_guard_ms,
        (tonumber(state.task_update_wait_until) or current_time) + 3500
    )
    logger(ctx).info(string.format(
        "[Leveling] route point action board post guard armed | task=%s key=%s remaining_ms=%d",
        tostring(M.current_task_log_name() or state.current_task_name or ""),
        tostring(active_key),
        math.max(0, (tonumber(state.route_point_action_board_guard_until) or current_time) - current_time)
    ))

    M.clear_route_point_action_board_state()
    return true
end

function M.maybe_handle_route_point_action_npc_dialogue(ctx, current_time, player_x, player_y, player_z)
    local active_key = tostring(state.route_point_action_dialogue_active_key or "")
    if active_key == "" and type(M.ROUTE_POINT_ACTIONS) == "table" then
        if M.is_task_combat_or_post_loot_active() then
            return false
        end
        if state.require_task_button_refresh == true
            or current_time < (tonumber(state.task_update_wait_until) or 0)
            or (tonumber(state.dialogue_escape_due_at) or 0) > 0
        then
            return false
        end
        if current_time < (tonumber(state.next_route_point_dialogue_scan_at) or 0) then
            return false
        end

        state.next_route_point_dialogue_scan_at = current_time + 700

        for _, action in ipairs(M.ROUTE_POINT_ACTIONS) do
            local mode = tostring(type(action) == "table" and action.mode or "")
            local dialogue = type(action) == "table" and action.dialogue or nil
            local trigger = type(action) == "table" and action.trigger or nil
            local trigger_x = tonumber(trigger and trigger.x)
            local trigger_y = tonumber(trigger and trigger.y)
            local trigger_z = tonumber(trigger and trigger.z)
            local trigger_radius = math.max(80, tonumber(trigger and trigger.radius) or 220)
            local z_tolerance = math.max(0, tonumber(trigger and trigger.z_tolerance) or 260)
            if mode == "npc_dialogue_point"
                and type(dialogue) == "table"
                and trigger_x ~= nil
                and trigger_y ~= nil
                and M.route_point_action_matches_task(action)
            then
                local trigger_distance = distance_2d(player_x, player_y, trigger_x, trigger_y)
                local z_gap = trigger_z ~= nil and type(player_z) == "number"
                    and math.abs((tonumber(player_z) or 0) - trigger_z)
                    or 0
                if trigger_distance <= trigger_radius and z_gap <= z_tolerance then
                    local state_key = M.route_point_action_state_key(action)
                    local last_triggered_at = tonumber(state.map_transition_triggered[state_key]) or 0
                    local retry_ms = math.max(1000, tonumber(action.retry_ms) or 4500)
                    if last_triggered_at > 0 and current_time - last_triggered_at < retry_ms then
                        return false
                    end
                    M.arm_route_point_action_npc_dialogue(ctx, action, state_key, current_time, "trigger")
                    active_key = tostring(state.route_point_action_dialogue_active_key or "")
                    break
                end
            end
        end
    end

    if active_key == "" then
        return false
    end

    local action = M.find_route_point_action_by_key(active_key)
    local dialogue = type(action) == "table" and action.dialogue or nil
    if type(action) ~= "table" or type(dialogue) ~= "table" then
        M.clear_route_point_action_npc_dialogue_state()
        return false
    end

    local deadline_at = tonumber(state.route_point_action_dialogue_deadline_at) or 0
    local state_key = tostring(state.route_point_action_dialogue_active_state_key or active_key)
    local point_x = tonumber(dialogue.x)
    local point_y = tonumber(dialogue.y)
    local point_z = tonumber(dialogue.z)
    local radius = math.max(80, tonumber(dialogue.radius) or 220)
    local interact_radius = math.max(60, tonumber(dialogue.interact_radius) or math.min(radius, 120))
    local z_tolerance = math.max(0, tonumber(dialogue.z_tolerance) or 260)
    local center_settle_ms = math.max(0, tonumber(dialogue.center_settle_ms) or 600)
    local interact_retry_ms = math.max(800, tonumber(dialogue.interact_retry_ms) or 1800)
    local npc_search_radius = math.max(interact_radius, tonumber(dialogue.npc_search_radius) or 420)

    if point_x == nil or point_y == nil then
        M.clear_route_point_action_npc_dialogue_state()
        return false
    end

    if deadline_at > 0 and current_time >= deadline_at then
        log_throttled(ctx, "route_point_action_npc_timeout_" .. tostring(state_key), "warn", LOG_THROTTLE_MS, string.format(
            "[Leveling] route point action npc dialogue timeout | task=%s key=%s point=%.2f, %.2f, %.2f",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(action.key or action.label or ""),
            tonumber(point_x) or 0,
            tonumber(point_y) or 0,
            tonumber(point_z) or 0
        ))
        M.clear_route_point_action_npc_dialogue_state()
        return false
    end

    local point_distance = distance_2d(player_x, player_y, point_x, point_y)
    local z_gap = point_z ~= nil and type(player_z) == "number"
        and math.abs((tonumber(player_z) or 0) - point_z)
        or 0

    if point_distance > interact_radius or z_gap > z_tolerance then
        state.stage = "route_point_action_npc_dialogue_move"
        hold_navigation(ctx, current_time, "route_point_action_npc_dialogue_move")
        if current_time >= (tonumber(state.next_move_at) or 0) then
            issue_move(ctx, current_time, {
                x = point_x,
                y = point_y,
                z = point_z,
                source = "route_point_npc_dialogue",
                path_index = 0,
                path_points = 0,
                move_interval_ms = math.max(120, tonumber(dialogue.move_interval_ms) or 220)
            })
        end
        log_throttled(ctx, "route_point_action_npc_move_" .. tostring(state_key), "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] route point action moving to npc dialogue point | task=%s key=%s distance=%.2f interact_radius=%.2f z_gap=%.2f point=%.2f, %.2f, %.2f",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(action.key or action.label or ""),
            tonumber(point_distance) or 0,
            tonumber(interact_radius) or 0,
            tonumber(z_gap) or 0,
            tonumber(point_x) or 0,
            tonumber(point_y) or 0,
            tonumber(point_z) or 0
        ))
        return true
    end

    state.stage = "route_point_action_npc_dialogue"
    hold_navigation(ctx, current_time, "route_point_action_npc_dialogue")
    if (tonumber(state.route_point_action_dialogue_entered_at) or 0) <= 0 then
        state.route_point_action_dialogue_entered_at = current_time
    end

    local settle_elapsed = current_time - (tonumber(state.route_point_action_dialogue_entered_at) or current_time)
    if settle_elapsed < center_settle_ms then
        log_throttled(ctx, "route_point_action_npc_settle_" .. tostring(state_key), "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] route point action waiting npc dialogue settle | task=%s key=%s settle_left=%dms",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(action.key or action.label or ""),
            math.max(0, center_settle_ms - settle_elapsed)
        ))
        return true
    end

    if current_time < (tonumber(state.route_point_action_dialogue_next_interact_at) or 0) then
        return true
    end

    local nearest_npc, npc_err = M.find_npc_near_point(ctx, point_x, point_y, npc_search_radius, player_x, player_y)
    if type(nearest_npc) == "table" then
        local ok, err = M.interact_with_npc(ctx, current_time, nearest_npc, {
            combat_retry_source = "route_point_action_npc_dialogue",
            combat_retry_route_action_key = tostring(action.key or action.label or ""),
            npc_label = tostring(nearest_npc.label or action.label or action.key or ""),
            point_x = point_x,
            point_y = point_y,
            point_z = point_z,
            search_radius = npc_search_radius,
            interact_radius = interact_radius,
            move_interval_ms = math.max(120, tonumber(dialogue.move_interval_ms) or 220),
            combat_retry_timeout_ms = math.max(6000, tonumber(dialogue.timeout_ms) or 18000)
        })
        state.route_point_action_dialogue_next_interact_at = current_time + interact_retry_ms
        if ok then
            state.map_transition_triggered[state_key] = current_time
            M.clear_route_point_action_npc_dialogue_state()
            logger(ctx).info(string.format(
                "[Leveling] route point action npc dialogue interact | task=%s key=%s npc=%s point_distance=%.2f player_distance=%.2f",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(action.key or action.label or ""),
                tostring(nearest_npc.label or ""),
                tonumber(nearest_npc.point_distance) or 0,
                tonumber(nearest_npc.distance) or 0
            ))
            return true
        end
        log_throttled(ctx, "route_point_action_npc_interact_failed_" .. tostring(state_key), "warn", LOG_THROTTLE_MS,
            "[Leveling] route point action npc dialogue failed: " .. tostring(err or "unknown"))
        return true
    end

    if dialogue.fallback_interact == true then
        local prompt_ok = M.interact_with_prompt(ctx, current_time, {
            related_text = tostring(action.label or action.key or "route_point_npc_dialogue"),
            name = "route point npc dialogue fallback",
            x = point_x,
            y = point_y
        }, 0, "route_point_npc_dialogue", false, true)
        state.route_point_action_dialogue_next_interact_at = current_time + interact_retry_ms
        if prompt_ok then
            state.map_transition_triggered[state_key] = current_time
            M.clear_route_point_action_npc_dialogue_state()
            logger(ctx).info(string.format(
                "[Leveling] route point action npc dialogue fallback interact | task=%s key=%s point=%.2f, %.2f",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(action.key or action.label or ""),
                tonumber(point_x) or 0,
                tonumber(point_y) or 0
            ))
            return true
        end
    end

    log_throttled(ctx, "route_point_action_npc_wait_" .. tostring(state_key), "info", LOG_THROTTLE_MS, string.format(
        "[Leveling] route point action waiting npc dialogue target | task=%s key=%s err=%s point=%.2f, %.2f radius=%.2f",
        tostring(M.current_task_log_name() or state.current_task_name or ""),
        tostring(action.key or action.label or ""),
        tostring(npc_err or ""),
        tonumber(point_x) or 0,
        tonumber(point_y) or 0,
        tonumber(npc_search_radius) or 0
    ))
    state.route_point_action_dialogue_next_interact_at = current_time + interact_retry_ms
    return true
end

function M.maybe_handle_route_point_action_route(ctx, current_time, player_x, player_y, player_z)
    local active_key = tostring(state.route_point_action_route_active_key or "")
    if active_key == "" then
        return false
    end

    local action = M.find_route_point_action_by_key(active_key)
    local waypoints = type(action) == "table" and action.waypoints or nil
    local state_key = tostring(state.route_point_action_route_active_state_key or active_key)
    if type(action) ~= "table" or type(waypoints) ~= "table" or #waypoints <= 0 then
        logger(ctx).warn(string.format(
            "[Leveling] route point action recorded route cleared: invalid config | task=%s key=%s",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(active_key)
        ))
        M.clear_route_point_action_route_state()
        return false
    end

    local skip_reason, skip_detail = M.route_point_action_skip_reason(ctx, action, current_time)
    if skip_reason ~= nil then
        logger(ctx).info(string.format(
            "[Leveling] route point action recorded route skipped | task=%s key=%s reason=%s detail=%s",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(active_key),
            tostring(skip_reason),
            tostring(skip_detail or "")
        ))
        M.clear_route_point_action_route_state()
        clear_task_target_state()
        return false
    end

    if not M.route_point_action_matches_task(action) then
        logger(ctx).info(string.format(
            "[Leveling] route point action recorded route cleared: task mismatch | task=%s key=%s",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(active_key)
        ))
        M.clear_route_point_action_route_state()
        return false
    end

    local deadline_at = tonumber(state.route_point_action_route_deadline_at) or 0
    if deadline_at > 0 and current_time >= deadline_at then
        logger(ctx).warn(string.format(
            "[Leveling] route point action recorded route timeout | task=%s key=%s index=%d/%d elapsed=%dms",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(active_key),
            math.max(1, tonumber(state.route_point_action_route_index) or 1),
            #waypoints,
            math.max(0, current_time - (tonumber(state.route_point_action_route_started_at) or current_time))
        ))
        M.clear_route_point_action_route_state()
        return false
    end

    local index = math.max(1, tonumber(state.route_point_action_route_index) or 1)
    while index <= #waypoints do
        local point = waypoints[index]
        local point_x = tonumber(type(point) == "table" and point.x)
        local point_y = tonumber(type(point) == "table" and point.y)
        if point_x ~= nil and point_y ~= nil then
            break
        end
        logger(ctx).warn(string.format(
            "[Leveling] route point action recorded route skipped invalid waypoint | task=%s key=%s index=%d/%d",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(active_key),
            index,
            #waypoints
        ))
        index = index + 1
        state.route_point_action_route_index = index
    end

    if index > #waypoints then
        state.stage = "route_point_action_route_reacquire"
        hold_navigation(ctx, current_time, "route_point_action_route_reacquire")
        release_async_combat_inputs(ctx, current_time, true)

        local next_retry_at = tonumber(state.route_point_action_route_next_retry_at) or 0
        local reacquire_retry_ms = math.max(800, tonumber(action.reacquire_retry_ms) or 1200)
        if current_time < next_retry_at then
            log_throttled(
                ctx,
                "route_point_action_route_reacquire_wait_" .. tostring(state_key),
                "info",
                LOG_THROTTLE_MS,
                string.format(
                    "[Leveling] route point action recorded route waiting task reacquire | task=%s key=%s retry_in=%dms",
                    tostring(M.current_task_log_name() or state.current_task_name or ""),
                    tostring(active_key),
                    math.max(0, next_retry_at - current_time)
                )
            )
            return true
        end

        local clicked, click_err = M.click_main_task_button(ctx, current_time)
        state.route_point_action_route_next_retry_at = math.max(
            current_time + reacquire_retry_ms,
            tonumber(state.next_task_button_click_at) or 0
        )
        if clicked then
            state.map_transition_triggered[state_key] = current_time
            local armed_entry_action = false
            if action.arm_task_entry_action_after_click == true then
                M.clear_route_point_action_route_state()
                local arm_err = nil
                armed_entry_action, arm_err = M.arm_task_entry_action_after_route_point(ctx, current_time, action)
                if not armed_entry_action then
                    log_throttled(
                        ctx,
                        "route_point_action_route_entry_arm_failed_" .. tostring(state_key),
                        "warn",
                        LOG_THROTTLE_MS,
                        string.format(
                            "[Leveling] route point action recorded route entry arm failed | task=%s key=%s err=%s",
                            tostring(M.current_task_log_name() or state.current_task_name or ""),
                            tostring(active_key),
                            tostring(arm_err or "")
                        )
                    )
                end
            end
            local wait_task_refresh_ms = math.max(
                0,
                tonumber(action.wait_task_refresh_before_reacquire_ms)
                    or tonumber(action.wait_task_refresh_ms)
                    or 0
            )
            logger(ctx).info(string.format(
                "[Leveling] route point action recorded route completed | task=%s key=%s points=%d next=%s",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(active_key),
                #waypoints,
                armed_entry_action and "task_entry_action"
                    or (wait_task_refresh_ms > 0 and "wait_task_refresh" or "main_task_reacquire")
            ))
            if not armed_entry_action then
                M.clear_route_point_action_route_state()
                if wait_task_refresh_ms > 0 then
                    M.clear_route_point_action_route_wait_state()
                    clear_task_target_state()
                    state.last_task_button_click_at = 0
                    state.next_task_button_click_at = math.max(
                        tonumber(state.next_task_button_click_at) or 0,
                        current_time + wait_task_refresh_ms
                    )
                    state.next_task_refresh_at = math.max(
                        tonumber(state.next_task_refresh_at) or 0,
                        current_time + wait_task_refresh_ms
                    )
                    state.route_point_action_route_wait_reacquire_until = current_time + wait_task_refresh_ms
                    state.route_point_action_route_wait_key = tostring(active_key)
                    state.route_point_action_route_wait_task_name = tostring(M.current_task_log_name() or state.current_task_name or "")
                    state.route_point_action_route_wait_task_detail = tostring(M.current_task_log_detail() or state.current_task_detail or "")
                    logger(ctx).info(string.format(
                        "[Leveling] route point action route completed, waiting task refresh before reacquire | task=%s key=%s wait_ms=%d",
                        tostring(M.current_task_log_name() or state.current_task_name or ""),
                        tostring(active_key),
                        wait_task_refresh_ms
                    ))
                else
                    logger(ctx).info(string.format(
                        "[Leveling] route point action released to main task path | task=%s key=%s task_path_wait_left=%dms next_refresh_in=%dms old_path_points=%d",
                        tostring(M.current_task_log_name() or state.current_task_name or ""),
                        tostring(active_key),
                        math.max(0, (tonumber(state.task_path_wait_until) or current_time) - current_time),
                        math.max(0, (tonumber(state.next_task_refresh_at) or current_time) - current_time),
                        tonumber(state.task_path_count) or 0
                    ))
                end
            end
            return true
        end

        local click_err_text = tostring(click_err or "")
        local retry_log_level = click_err_text:find("cooldown", 1, true) ~= nil and "info" or "warn"
        log_throttled(
            ctx,
            "route_point_action_route_reacquire_failed_" .. tostring(state_key),
            retry_log_level,
            LOG_THROTTLE_MS,
            string.format(
                "[Leveling] route point action recorded route task reacquire failed | task=%s key=%s err=%s",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(active_key),
                click_err_text
            )
        )
        return true
    end

    local point = waypoints[index]
    local point_x = tonumber(point.x)
    local point_y = tonumber(point.y)
    local point_z = tonumber(point.z)
    local reach_radius = math.max(80, tonumber(point.reach_radius) or tonumber(action.waypoint_reach_radius) or 180)
    local z_tolerance = math.max(0, tonumber(point.z_tolerance) or tonumber(action.waypoint_z_tolerance) or 260)
    local point_distance = distance_2d(player_x, player_y, point_x, point_y)
    local z_gap = point_z ~= nil and type(player_z) == "number"
        and math.abs((tonumber(player_z) or 0) - point_z)
        or 0

    if point_distance <= reach_radius and z_gap <= z_tolerance then
        state.route_point_action_route_index = index + 1
        state.route_point_action_route_next_retry_at = current_time
        logger(ctx).info(string.format(
            "[Leveling] route point action recorded waypoint reached | task=%s key=%s index=%d/%d distance=%.2f z_gap=%.2f point=%.2f, %.2f, %.2f",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(active_key),
            index,
            #waypoints,
            tonumber(point_distance) or 0,
            tonumber(z_gap) or 0,
            tonumber(point_x) or 0,
            tonumber(point_y) or 0,
            tonumber(point_z) or 0
        ))
        index = index + 1
        if index <= #waypoints then
            return true
        end
    end

    if index > #waypoints then
        return M.maybe_handle_route_point_action_route(ctx, current_time, player_x, player_y, player_z)
    end

    point = waypoints[index]
    point_x = tonumber(point.x)
    point_y = tonumber(point.y)
    point_z = tonumber(point.z)
    reach_radius = math.max(80, tonumber(point.reach_radius) or tonumber(action.waypoint_reach_radius) or 180)
    z_tolerance = math.max(0, tonumber(point.z_tolerance) or tonumber(action.waypoint_z_tolerance) or 260)
    point_distance = distance_2d(player_x, player_y, point_x, point_y)
    z_gap = point_z ~= nil and type(player_z) == "number"
        and math.abs((tonumber(player_z) or 0) - point_z)
        or 0

    state.stage = "route_point_action_route_move"
    hold_navigation(ctx, current_time, "route_point_action_route_move")
    release_async_combat_inputs(ctx, current_time, true)

    local move_interval_ms = math.max(120, tonumber(point.move_interval_ms) or tonumber(action.move_interval_ms) or 220)
    if current_time >= (tonumber(state.route_point_action_route_next_retry_at) or 0) then
        issue_move(ctx, current_time, {
            x = point_x,
            y = point_y,
            z = point_z,
            source = "route_point_recorded_route",
            path_index = index,
            path_points = #waypoints,
            move_interval_ms = move_interval_ms
        })
        state.route_point_action_route_next_retry_at = current_time + move_interval_ms
    end

    log_throttled(
        ctx,
        "route_point_action_route_move_" .. tostring(state_key) .. "_" .. tostring(index),
        "info",
        900,
        string.format(
            "[Leveling] route point action moving along recorded route | task=%s key=%s index=%d/%d distance=%.2f reach_radius=%.2f z_gap=%.2f point=%.2f, %.2f, %.2f",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(active_key),
            index,
            #waypoints,
            tonumber(point_distance) or 0,
            tonumber(reach_radius) or 0,
            tonumber(z_gap) or 0,
            tonumber(point_x) or 0,
            tonumber(point_y) or 0,
            tonumber(point_z) or 0
        )
    )
    M.maybe_issue_route_point_action_route_combat(
        ctx,
        current_time,
        player_x,
        player_y,
        point_distance,
        active_key
    )
    return true
end

function M.maybe_handle_route_point_action_objective(ctx, current_time, player_x, player_y, player_z)
    local active_key = tostring(state.route_point_action_objective_active_key or "")
    if active_key == "" then
        return false
    end

    local action = M.find_route_point_action_by_key(active_key)
    if type(action) ~= "table" then
        M.clear_route_point_action_objective_state()
        return false
    end

    local trigger = type(action.trigger) == "table" and action.trigger or nil
    local step = type(action.step) == "table" and action.step or nil
    if type(trigger) ~= "table" or type(step) ~= "table" then
        M.clear_route_point_action_objective_state()
        return false
    end

    local trigger_x = tonumber(trigger.x)
    local trigger_y = tonumber(trigger.y)
    local trigger_z = tonumber(trigger.z)
    if trigger_x == nil or trigger_y == nil then
        M.clear_route_point_action_objective_state()
        return false
    end

    local destination_match, destination_distance, destination_err, destination = M.route_point_action_destination_matches(
        action,
        player_x,
        player_y
    )
    if action.require_destination_match == true and destination_match ~= true then
        log_throttled(
            ctx,
            "route_point_action_objective_destination_mismatch_" .. tostring(active_key),
            "info",
            LOG_THROTTLE_MS,
            string.format(
                "[Leveling] route point action objective destination mismatch | task=%s key=%s err=%s destination_distance=%s destination=%.2f, %.2f trigger=%.2f, %.2f",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(active_key),
                tostring(destination_err or ""),
                destination_distance ~= nil and string.format("%.2f", destination_distance) or "nil",
                tonumber(destination and destination.x) or 0,
                tonumber(destination and destination.y) or 0,
                tonumber(trigger_x) or 0,
                tonumber(trigger_y) or 0
            )
        )
        M.clear_route_point_action_objective_state()
        return false
    end

    local z_tolerance = math.max(0, tonumber(trigger.z_tolerance) or 260)
    local interact_radius = math.max(40, tonumber(action.interact_radius) or tonumber(step.hint_max_distance) or 120)
    local fallback_distance = math.max(interact_radius, tonumber(action.fallback_interact_distance) or interact_radius)
    local deadline_at = tonumber(state.route_point_action_objective_deadline_at) or 0
    if deadline_at > 0 and current_time >= deadline_at then
        logger(ctx).warn(string.format(
            "[Leveling] route point action objective timeout | task=%s key=%s point=%.2f, %.2f, %.2f",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(active_key),
            tonumber(trigger_x) or 0,
            tonumber(trigger_y) or 0,
            tonumber(trigger_z) or 0
        ))
        M.clear_route_point_action_objective_state()
        return false
    end

    local trigger_distance = distance_2d(player_x, player_y, trigger_x, trigger_y)
    local z_gap = trigger_z ~= nil and type(player_z) == "number"
        and math.abs((tonumber(player_z) or 0) - trigger_z)
        or 0

    if trigger_distance > interact_radius or z_gap > z_tolerance then
        state.stage = "route_point_action_objective_move"
        release_async_combat_inputs(ctx, current_time, true)
        issue_move(ctx, current_time, {
            x = trigger_x,
            y = trigger_y,
            z = trigger_z,
            source = "route_point_objective",
            path_index = 0
        })
        log_throttled(
            ctx,
            "route_point_action_objective_move_" .. tostring(active_key),
            "info",
            LOG_THROTTLE_MS,
            string.format(
                "[Leveling] route point action moving to objective point | task=%s key=%s distance=%.2f interact_radius=%.2f z_gap=%.2f point=%.2f, %.2f, %.2f",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(active_key),
                tonumber(trigger_distance) or 0,
                tonumber(interact_radius) or 0,
                tonumber(z_gap) or 0,
                tonumber(trigger_x) or 0,
                tonumber(trigger_y) or 0,
                tonumber(trigger_z) or 0
            )
        )
        return true
    end

    state.stage = "route_point_action_objective_wait"
    hold_navigation(ctx, current_time, "route_point_action_objective")
    release_async_combat_inputs(ctx, current_time, true)

    local next_probe_at = tonumber(state.route_point_action_objective_next_probe_at) or 0
    if current_time < next_probe_at then
        log_throttled(
            ctx,
            "route_point_action_objective_settle_" .. tostring(active_key),
            "info",
            LOG_THROTTLE_MS,
            string.format(
                "[Leveling] route point action objective holding at point | task=%s key=%s distance=%.2f probe_in=%dms",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(active_key),
                tonumber(trigger_distance) or 0,
                math.max(0, math.floor(next_probe_at - current_time))
            )
        )
        return true
    end

    local target, fetch_err = fetch_locator_button_target(ctx, step)
    if target then
        local clicked, click_err, retryable = click_locator_button_target(ctx, step, target)
        if clicked then
            local state_key = tostring(state.route_point_action_objective_active_state_key or active_key)
            state.map_transition_triggered[state_key] = current_time
            M.clear_route_point_action_objective_state()
            local armed_entry_action = false
            if action.arm_task_entry_action_after_click == true then
                local arm_err = nil
                armed_entry_action, arm_err = M.arm_task_entry_action_after_route_point(ctx, current_time, action)
                if not armed_entry_action then
                    log_throttled(
                        ctx,
                        "route_point_action_entry_arm_failed_" .. tostring(active_key),
                        "warn",
                        LOG_THROTTLE_MS,
                        string.format(
                            "[Leveling] route point action entry arm failed | task=%s key=%s err=%s",
                            tostring(M.current_task_log_name() or state.current_task_name or ""),
                            tostring(active_key),
                            tostring(arm_err or "")
                        )
                    )
                end
            end
            if not armed_entry_action then
                local refresh_opts = nil
                if action.force_task_call_after_transition == true then
                    refresh_opts = {
                        force_task_call = true,
                        task_pos_reject_extra_ms = tonumber(action.task_pos_reject_extra_ms) or nil
                    }
                end
                schedule_task_refresh_after_transition(
                    ctx,
                    current_time,
                    "route_point_action_objective_" .. tostring(action.key or ""),
                    tonumber(action.settle_ms) or POST_DIALOGUE_SETTLE_MS,
                    refresh_opts
                )
            end
            logger(ctx).info(string.format(
                "[Leveling] route point action objective clicked | task=%s key=%s label=%s distance=%.2f z_gap=%.2f pos=%.2f, %.2f, %.2f next=%s",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(action.key or ""),
                tostring(step.label or action.label or ""),
                tonumber(trigger_distance) or 0,
                tonumber(z_gap) or 0,
                tonumber(player_x) or 0,
                tonumber(player_y) or 0,
                tonumber(player_z) or 0,
                armed_entry_action and "task_entry_action" or "main_task_refresh"
            ))
            return true
        end

        state.route_point_action_objective_next_probe_at = current_time + math.max(400, tonumber(action.probe_retry_ms) or 800)
        log_throttled(
            ctx,
            retryable and ("route_point_action_objective_retry_" .. tostring(active_key))
                or ("route_point_action_objective_failed_" .. tostring(active_key)),
            retryable and "info" or "warn",
            LOG_THROTTLE_MS,
            string.format(
                "[Leveling] route point action objective button unavailable | task=%s key=%s distance=%.2f z_gap=%.2f err=%s",
                tostring(M.current_task_log_name() or state.current_task_name or ""),
                tostring(active_key),
                tonumber(trigger_distance) or 0,
                tonumber(z_gap) or 0,
                tostring(click_err or "")
            )
        )
        return true
    end

    if action.fallback_interact == true and trigger_distance <= fallback_distance then
        local state_key = tostring(state.route_point_action_objective_active_state_key or active_key)
        local attempted_at = tonumber(state.route_point_action_attempted[state_key]) or 0
        local fallback_retry_ms = math.max(1000, tonumber(action.fallback_retry_ms) or 2500)
        if current_time - attempted_at >= fallback_retry_ms then
            state.route_point_action_attempted[state_key] = current_time
            local ok, err = press_keyboard_hotkey(ctx, current_time, VK_D, "leveling route point objective")
            if ok then
                state.map_transition_triggered[state_key] = current_time
                M.clear_route_point_action_objective_state()
                schedule_task_refresh_after_transition(
                    ctx,
                    current_time,
                    "route_point_action_objective_interact_" .. tostring(action.key or ""),
                    tonumber(action.settle_ms) or POST_DIALOGUE_SETTLE_MS
                )
                logger(ctx).info(string.format(
                    "[Leveling] route point action objective interact fallback | task=%s key=%s distance=%.2f z_gap=%.2f pos=%.2f, %.2f, %.2f next=main_task_refresh",
                    tostring(M.current_task_log_name() or state.current_task_name or ""),
                    tostring(active_key),
                    tonumber(trigger_distance) or 0,
                    tonumber(z_gap) or 0,
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
                return true
            end
            log_throttled(
                ctx,
                "route_point_action_objective_interact_failed_" .. tostring(active_key),
                "warn",
                LOG_THROTTLE_MS,
                "[Leveling] route point action objective interact failed: " .. tostring(err or "")
            )
        end
    end

    state.route_point_action_objective_next_probe_at = current_time + math.max(400, tonumber(action.probe_retry_ms) or 800)
    log_throttled(
        ctx,
        "route_point_action_objective_wait_" .. tostring(active_key),
        "info",
        LOG_THROTTLE_MS,
        string.format(
            "[Leveling] route point action objective waiting button | task=%s key=%s distance=%.2f z_gap=%.2f err=%s",
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(active_key),
            tonumber(trigger_distance) or 0,
            tonumber(z_gap) or 0,
            tostring(fetch_err or "")
        )
    )
    return true
end

function M.maybe_handle_route_point_action(ctx, current_time, player_x, player_y, player_z, opts)
    if type(M.ROUTE_POINT_ACTIONS) ~= "table" or #M.ROUTE_POINT_ACTIONS == 0 then
        return false
    end
    opts = type(opts) == "table" and opts or {}
    local require_allow_without_task_target = opts.require_allow_without_task_target == true
    local require_wait_task_path_recover = opts.require_wait_task_path_recover == true
    if M.is_task_combat_or_post_loot_active() then
        return false
    end
    if M.maybe_handle_route_point_action_route(ctx, current_time, player_x, player_y, player_z) then
        return true
    end
    if M.maybe_handle_route_point_action_objective(ctx, current_time, player_x, player_y, player_z) then
        return true
    end
    if state.require_task_button_refresh == true
        or current_time < (tonumber(state.task_update_wait_until) or 0)
        or (tonumber(state.dialogue_escape_due_at) or 0) > 0
    then
        return false
    end
    if current_time < (tonumber(state.next_route_point_action_scan_at) or 0) then
        return false
    end

    state.next_route_point_action_scan_at = current_time + 900

    for _, action in ipairs(M.ROUTE_POINT_ACTIONS) do
        local action_allows_without_task_target = type(action) == "table" and action.allow_without_task_target == true
        local action_allows_wait_task_path_recover = type(action) == "table" and action.allow_wait_task_path_recover == true
        if not (require_allow_without_task_target
            and not (
                action_allows_without_task_target
                and (not require_wait_task_path_recover or action_allows_wait_task_path_recover)
            ))
        then
            local trigger = type(action) == "table" and action.trigger or nil
            local step = type(action) == "table" and action.step or nil
            local action_task = M.current_task_log_name() or state.current_task_name or ""
            local action_detail = M.current_task_log_detail() or state.current_task_detail or ""
            local trigger_x = tonumber(trigger and trigger.x)
            local trigger_y = tonumber(trigger and trigger.y)
            local trigger_z = tonumber(trigger and trigger.z)
            local trigger_radius = math.max(80, tonumber(trigger and trigger.radius) or 220)
            local z_tolerance = math.max(0, tonumber(trigger and trigger.z_tolerance) or 260)
            if trigger_x ~= nil
                and trigger_y ~= nil
                and M.route_point_action_matches_task(action)
            then
                local skip_reason, skip_detail = M.route_point_action_skip_reason(ctx, action, current_time)
                if skip_reason ~= nil then
                    log_throttled(
                        ctx,
                        "route_point_action_skip_" .. tostring(action.key or ""),
                        "info",
                        LOG_THROTTLE_MS,
                        string.format(
                            "[Leveling] route point action skipped | task=%s detail=%s key=%s reason=%s detail=%s",
                            tostring(action_task),
                            tostring(action_detail),
                            tostring(action.key or ""),
                            tostring(skip_reason),
                            tostring(skip_detail or "")
                        )
                    )
                    return false
                end
                local trigger_distance = distance_2d(player_x, player_y, trigger_x, trigger_y)
                local z_gap = trigger_z ~= nil and type(player_z) == "number"
                    and math.abs((tonumber(player_z) or 0) - trigger_z)
                    or 0
                local destination_match, destination_distance, destination_err, destination = M.route_point_action_destination_matches(
                    action,
                    player_x,
                    player_y
                )
                if trigger_distance <= trigger_radius and z_gap <= z_tolerance then
                    local map_match, current_map_name, map_err = M.route_point_action_matches_current_map(
                        ctx,
                        action,
                        current_time
                    )
                    if map_match ~= true then
                        log_throttled(
                            ctx,
                            "route_point_action_map_mismatch_" .. tostring(action.key or ""),
                            "info",
                            LOG_THROTTLE_MS,
                            string.format(
                                "[Leveling] route point action map mismatch | task=%s detail=%s key=%s map=%s err=%s",
                                tostring(action_task),
                                tostring(action_detail),
                                tostring(action.key or ""),
                                tostring(current_map_name or ""),
                                tostring(map_err or "")
                            )
                        )
                        return false
                    end
                    if action.require_destination_match == true and destination_match ~= true then
                        log_throttled(
                            ctx,
                            "route_point_action_destination_mismatch_" .. tostring(action.key or ""),
                            "info",
                            LOG_THROTTLE_MS,
                            string.format(
                                "[Leveling] route point action destination mismatch | task=%s detail=%s key=%s distance=%.2f destination_distance=%s destination=%.2f, %.2f trigger=%.2f, %.2f",
                                tostring(action_task),
                                tostring(action_detail),
                                tostring(action.key or ""),
                                tonumber(trigger_distance) or 0,
                                destination_distance ~= nil and string.format("%.2f", destination_distance) or tostring(destination_err or "nil"),
                                tonumber(destination and destination.x) or 0,
                                tonumber(destination and destination.y) or 0,
                                tonumber(trigger_x) or 0,
                                tonumber(trigger_y) or 0
                            )
                        )
                        return false
                    end
                log_throttled(
                    ctx,
                    "route_point_action_trigger_" .. tostring(action.key or ""),
                    "info",
                    LOG_THROTTLE_MS,
                    string.format(
                        "[Leveling] route point action trigger matched | task=%s detail=%s key=%s mode=%s distance=%.2f z_gap=%.2f trigger=%.2f, %.2f, %.2f radius=%.2f",
                        tostring(action_task),
                        tostring(action_detail),
                        tostring(action.key or ""),
                        tostring(action.mode or ""),
                        tonumber(trigger_distance) or 0,
                        tonumber(z_gap) or 0,
                        tonumber(trigger_x) or 0,
                        tonumber(trigger_y) or 0,
                        tonumber(trigger_z) or 0,
                        tonumber(trigger_radius) or 0
                    )
                )
                local state_key = M.route_point_action_state_key(action)
                local last_triggered_at = tonumber(state.map_transition_triggered[state_key]) or 0
                local retry_ms = math.max(1000, tonumber(action.retry_ms) or 3000)
                if last_triggered_at > 0 and current_time - last_triggered_at < retry_ms then
                    return false
                end

                if tostring(action.mode or "") == "objective_button_flow_point" then
                    local armed_action, arm_err = M.activate_route_point_action(
                        ctx,
                        current_time,
                        tostring(action.key or action.label or ""),
                        "route_point_trigger"
                    )
                    if type(armed_action) == "table" then
                        return M.maybe_handle_route_point_action_objective(ctx, current_time, player_x, player_y, player_z)
                    end
                    log_throttled(
                        ctx,
                        "route_point_action_objective_arm_failed_" .. tostring(action.key or ""),
                        "warn",
                        LOG_THROTTLE_MS,
                        string.format(
                            "[Leveling] route point action objective arm failed | task=%s detail=%s key=%s err=%s",
                            tostring(action_task),
                            tostring(action_detail),
                            tostring(action.key or ""),
                            tostring(arm_err or "")
                        )
                    )
                    return false
                end

                if tostring(action.mode or "") == "recorded_route_point" then
                    local armed_action, arm_err = M.activate_route_point_action(
                        ctx,
                        current_time,
                        tostring(action.key or action.label or ""),
                        "route_point_trigger"
                    )
                    if type(armed_action) == "table" then
                        return M.maybe_handle_route_point_action_route(ctx, current_time, player_x, player_y, player_z)
                    end
                    log_throttled(
                        ctx,
                        "route_point_action_route_arm_failed_" .. tostring(action.key or ""),
                        "warn",
                        LOG_THROTTLE_MS,
                        string.format(
                            "[Leveling] route point action recorded route arm failed | task=%s detail=%s key=%s err=%s",
                            tostring(action_task),
                            tostring(action_detail),
                            tostring(action.key or ""),
                            tostring(arm_err or "")
                        )
                    )
                    return false
                end

                if type(step) == "table" then
                    local target, fetch_err = fetch_locator_button_target(ctx, step)
                    if target then
                        state.stage = "route_point_action"
                        hold_navigation(ctx, current_time, "route_point_action")
                        release_async_combat_inputs(ctx, current_time, true)
                        local clicked, click_err, retryable = click_locator_button_target(ctx, step, target)
                        if clicked then
                            state.map_transition_triggered[state_key] = current_time
                            if type(action.board) == "table" then
                                M.arm_route_point_action_board(ctx, action, state_key, current_time, "button_click")
                            else
                                schedule_task_refresh_after_transition(
                                    ctx,
                                    current_time,
                                    "route_point_action_" .. tostring(action.key or ""),
                                    tonumber(action.settle_ms) or POST_DIALOGUE_SETTLE_MS,
                                    action.force_task_call_after_transition == true and {
                                        force_task_call = true,
                                        task_pos_reject_extra_ms = tonumber(action.task_pos_reject_extra_ms) or 2500
                                    } or nil
                                )
                            end
                            logger(ctx).info(string.format(
                                "[Leveling] route point action clicked | task=%s detail=%s key=%s mode=%s label=%s trigger_distance=%.2f z_gap=%.2f pos=%.2f, %.2f, %.2f",
                                tostring(action_task),
                                tostring(action_detail),
                                tostring(action.key or ""),
                                tostring(action.mode or ""),
                                tostring(step.label or action.label or ""),
                                tonumber(trigger_distance) or 0,
                                tonumber(z_gap) or 0,
                                tonumber(player_x) or 0,
                                tonumber(player_y) or 0,
                                tonumber(player_z) or 0
                            ))
                            return true
                        end
                        log_throttled(
                            ctx,
                            retryable and "route_point_action_retry" or "route_point_action_failed",
                            retryable and "info" or "warn",
                            LOG_THROTTLE_MS,
                            string.format(
                                "[Leveling] route point action button unavailable | task=%s detail=%s key=%s mode=%s distance=%.2f z_gap=%.2f err=%s",
                                tostring(action_task),
                                tostring(action_detail),
                                tostring(action.key or ""),
                                tostring(action.mode or ""),
                                tonumber(trigger_distance) or 0,
                                tonumber(z_gap) or 0,
                                tostring(click_err or "")
                            )
                        )
                    else
                        log_throttled(
                            ctx,
                            "route_point_action_wait_button",
                            "info",
                            LOG_THROTTLE_MS,
                            string.format(
                                "[Leveling] route point action waiting button | task=%s detail=%s key=%s mode=%s distance=%.2f z_gap=%.2f err=%s",
                                tostring(action_task),
                                tostring(action_detail),
                                tostring(action.key or ""),
                                tostring(action.mode or ""),
                                tonumber(trigger_distance) or 0,
                                tonumber(z_gap) or 0,
                                tostring(fetch_err or "")
                            )
                        )
                    end
                end

                if action.fallback_interact == true then
                    local attempted_at = tonumber(state.route_point_action_attempted[state_key]) or 0
                    local fallback_retry_ms = math.max(1000, tonumber(action.fallback_retry_ms) or 2500)
                    local fallback_distance = math.max(80, tonumber(action.fallback_interact_distance) or trigger_radius)
                    if trigger_distance <= fallback_distance and current_time - attempted_at >= fallback_retry_ms then
                        state.route_point_action_attempted[state_key] = current_time
                        state.stage = "route_point_action_interact"
                        hold_navigation(ctx, current_time, "route_point_action_interact")
                        release_async_combat_inputs(ctx, current_time, true)
                        local ok, err = press_keyboard_hotkey(ctx, current_time, VK_D, "leveling route point action")
                        if ok then
                            state.map_transition_triggered[state_key] = current_time
                            if type(action.board) == "table" then
                                M.arm_route_point_action_board(ctx, action, state_key, current_time, "interact_fallback")
                            else
                                schedule_task_refresh_after_transition(
                                    ctx,
                                    current_time,
                                    "route_point_action_interact_" .. tostring(action.key or ""),
                                    tonumber(action.settle_ms) or POST_DIALOGUE_SETTLE_MS,
                                    action.force_task_call_after_transition == true and {
                                        force_task_call = true,
                                        task_pos_reject_extra_ms = tonumber(action.task_pos_reject_extra_ms) or 2500
                                    } or nil
                                )
                            end
                            logger(ctx).info(string.format(
                                "[Leveling] route point action interact fallback | task=%s detail=%s key=%s mode=%s distance=%.2f z_gap=%.2f pos=%.2f, %.2f, %.2f",
                                tostring(action_task),
                                tostring(action_detail),
                                tostring(action.key or ""),
                                tostring(action.mode or ""),
                                tonumber(trigger_distance) or 0,
                                tonumber(z_gap) or 0,
                                tonumber(player_x) or 0,
                                tonumber(player_y) or 0,
                                tonumber(player_z) or 0
                            ))
                            return true
                        end
                        log_throttled(
                            ctx,
                            "route_point_action_interact_failed",
                            "warn",
                            LOG_THROTTLE_MS,
                            "[Leveling] route point action interact failed: " .. tostring(err or "")
                        )
                    end
                end
            end
        end
    end
    end

    return false
end

local function try_click_map_transition(ctx, current_time, map_name, transition, target, trigger_distance)
    local step = type(transition) == "table" and transition.step or nil
    if type(step) ~= "table" then
        return false, "map transition step is unavailable."
    end

    release_async_combat_inputs(ctx, current_time, true)
    local clicked, click_err, retryable = click_locator_button_target(ctx, step, target)
    if not clicked then
        if retryable then
            log_throttled(ctx, "map_transition_click_retry", "warn", LOG_THROTTLE_MS,
                "[Leveling] map transition click retry: " .. tostring(click_err))
            return false, click_err
        end
        log_throttled(ctx, "map_transition_click_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] map transition click failed: " .. tostring(click_err))
        return false, click_err
    end

    state.map_transition_triggered[map_transition_state_key(map_name, transition)] = current_time
    schedule_task_refresh_after_transition(
        ctx,
        current_time,
        "map_transition_" .. tostring(transition.key or ""),
        tonumber(transition.settle_ms) or POST_DIALOGUE_SETTLE_MS,
        {
            force_task_call = true,
            task_pos_reject_extra_ms = tonumber(transition.task_pos_reject_extra_ms) or 3500
        }
    )
    logger(ctx).info(string.format(
        "[Leveling] map transition clicked | map=%s key=%s label=%s trigger_distance=%.2f pos=%.2f, %.2f",
        tostring(map_name or ""),
        tostring(transition.key or ""),
        tostring(type(step) == "table" and (step.label or transition.label) or ""),
        tonumber(trigger_distance) or 0,
        tonumber(target and target.x) or 0,
        tonumber(target and target.y) or 0
    ))
    return true
end

local function maybe_handle_map_specific_transition(ctx, current_time, player_x, player_y)
    if state.revive_reentry_pending == true and type(state.revive_reentry_cfg) == "table" then
        local revive_cfg = state.revive_reentry_cfg
        local deadline_at = tonumber(state.revive_reentry_deadline_at) or 0
        if deadline_at > 0 and current_time >= deadline_at then
            log_throttled(ctx, "revive_boss_reentry_timeout", "warn", LOG_THROTTLE_MS, string.format(
                "[Leveling] revive boss reentry timed out | source=%s objective_key=%s label=%s timeout_ms=%d",
                tostring(state.revive_reentry_source or ""),
                tostring(state.revive_reentry_objective_key or ""),
                tostring(revive_cfg.label or revive_cfg.key or ""),
                math.max(0, deadline_at - (tonumber(state.revive_started_at) or deadline_at))
            ))
            M.clear_revive_reentry_state()
            return false
        end

        if state.require_task_button_refresh == true or (tonumber(state.task_update_wait_until) or 0) > current_time then
            return false
        end

        local anchor = type(revive_cfg.anchor) == "table" and revive_cfg.anchor or revive_cfg
        local anchor_x = tonumber(anchor and anchor.x)
        local anchor_y = tonumber(anchor and anchor.y)
        local anchor_z = tonumber(anchor and anchor.z)
        local interact_distance = math.max(140, tonumber(revive_cfg.interact_distance) or 260)
        if anchor_x == nil or anchor_y == nil then
            log_throttled(ctx, "revive_boss_reentry_invalid_anchor", "warn", LOG_THROTTLE_MS,
                "[Leveling] revive boss reentry missing anchor coordinates, disable pending state.")
            M.clear_revive_reentry_state()
            return false
        end

        local move_distance = distance_2d(player_x, player_y, anchor_x, anchor_y)
        local follow_task_path_to_anchor = revive_cfg.follow_task_path_to_anchor == true
            or revive_cfg.call_task_before_reentry == true
        local portal_scan_distance = math.max(
            interact_distance,
            tonumber(revive_cfg.portal_scan_distance)
                or tonumber(anchor.radius)
                or (interact_distance * 2)
        )
        local state_key = "revive_boss_reentry:" .. tostring(revive_cfg.key or state.revive_reentry_objective_key or "")
        local last_triggered_at = tonumber(state.map_transition_triggered[state_key]) or 0
        local retry_ms = math.max(800, tonumber(revive_cfg.retry_ms) or 1200)
        local step = type(revive_cfg.step) == "table" and revive_cfg.step
            or (revive_cfg.use_global_portal ~= false and M.GLOBAL_TASK_PORTAL_STEP or nil)

        if follow_task_path_to_anchor and move_distance > portal_scan_distance then
            if type(state.task_target) ~= "table" then
                if current_time >= (tonumber(state.next_task_button_click_at) or 0) then
                    state.stage = "revive_boss_reentry_call_task"
                    local clicked, click_err = M.click_main_task_button(ctx, current_time)
                    if clicked then
                        log_throttled(ctx, "revive_boss_reentry_call_task", "info", LOG_THROTTLE_MS, string.format(
                            "[Leveling] revive boss reentry called main task before portal path | source=%s objective_key=%s distance=%.2f",
                            tostring(state.revive_reentry_source or ""),
                            tostring(state.revive_reentry_objective_key or ""),
                            tonumber(move_distance) or 0
                        ))
                    else
                        log_throttled(ctx, "revive_boss_reentry_call_task_failed", "info", LOG_THROTTLE_MS,
                            "[Leveling] revive boss reentry waiting main task call before portal path: " .. tostring(click_err or "not ready"))
                    end
                    return true
                end
                return false
            end

            state.stage = "revive_boss_reentry_follow_task"
            log_throttled(ctx, "revive_boss_reentry_follow_task", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] revive boss reentry following task path to portal | source=%s objective_key=%s anchor=%.2f, %.2f distance=%.2f scan_distance=%.2f",
                tostring(state.revive_reentry_source or ""),
                tostring(state.revive_reentry_objective_key or ""),
                tonumber(anchor_x) or 0,
                tonumber(anchor_y) or 0,
                tonumber(move_distance) or 0,
                tonumber(portal_scan_distance) or 0
            ))
            return false
        end

        if not follow_task_path_to_anchor and move_distance > interact_distance then
            state.stage = "revive_boss_reentry_move"
            issue_move(ctx, current_time, {
                x = anchor_x,
                y = anchor_y,
                z = anchor_z,
                source = "revive_boss_reentry",
                path_index = 0,
                path_points = tonumber(state.task_path_count) or 0
            })
            log_throttled(ctx, "revive_boss_reentry_move", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] revive boss reentry moving to portal point | source=%s objective_key=%s target=%.2f, %.2f distance=%.2f",
                tostring(state.revive_reentry_source or ""),
                tostring(state.revive_reentry_objective_key or ""),
                tonumber(anchor_x) or 0,
                tonumber(anchor_y) or 0,
                tonumber(move_distance) or 0
            ))
            return true
        end

        if last_triggered_at > 0 and current_time - last_triggered_at < retry_ms then
            return true
        end

        if type(step) == "table" then
            local target, fetch_err = fetch_locator_button_target(ctx, step)
            if type(target) == "table" then
                release_async_combat_inputs(ctx, current_time, true)
                local clicked, click_err, retryable = click_locator_button_target(ctx, step, target)
                if clicked then
                    local engage_ms = math.max(6000, tonumber(revive_cfg.post_transition_boss_engage_ms) or 15000)
                    state.map_transition_triggered[state_key] = current_time
                    state.post_revive_boss_engage_until = math.max(
                        tonumber(state.post_revive_boss_engage_until) or 0,
                        current_time + engage_ms
                    )
                    M.clear_revive_reentry_state()
                    schedule_task_refresh_after_transition(
                        ctx,
                        current_time,
                        "map_transition_revive_boss_reentry_" .. tostring(revive_cfg.key or "portal"),
                        tonumber(revive_cfg.settle_ms) or POST_DIALOGUE_SETTLE_MS
                    )
                    logger(ctx).info(string.format(
                        "[Leveling] revive boss reentry portal clicked | source=%s objective_key=%s label=%s engage_window=%dms",
                        tostring(state.task_combat_locked_reentry_source or state.revive_reentry_source or ""),
                        tostring(state.task_combat_locked_objective_key or state.revive_reentry_objective_key or ""),
                        tostring(revive_cfg.label or revive_cfg.key or ""),
                        engage_ms
                    ))
                    return true
                end
                log_throttled(ctx,
                    retryable and "revive_boss_reentry_click_retry" or "revive_boss_reentry_click_failed",
                    retryable and "info" or "warn",
                    LOG_THROTTLE_MS,
                    "[Leveling] revive boss reentry portal click failed: " .. tostring(click_err))
                return true
            end
            log_throttled(ctx, "revive_boss_reentry_wait_portal", "info", LOG_THROTTLE_MS,
                "[Leveling] revive boss reentry waiting portal visibility: " .. tostring(fetch_err or "not visible"))
        end

        if revive_cfg.fallback_interact == true
            and (not follow_task_path_to_anchor or move_distance <= interact_distance)
        then
            state.stage = "revive_boss_reentry_interact"
            hold_navigation(ctx, current_time, "revive_boss_reentry_interact")
            release_async_combat_inputs(ctx, current_time, true)
            local ok, err = press_keyboard_hotkey(ctx, current_time, VK_D, "leveling revive boss reentry")
            if ok then
                local engage_ms = math.max(6000, tonumber(revive_cfg.post_transition_boss_engage_ms) or 15000)
                state.map_transition_triggered[state_key] = current_time
                state.post_revive_boss_engage_until = math.max(
                    tonumber(state.post_revive_boss_engage_until) or 0,
                    current_time + engage_ms
                )
                M.clear_revive_reentry_state()
                schedule_task_refresh_after_transition(
                    ctx,
                    current_time,
                    "map_transition_revive_boss_reentry_" .. tostring(revive_cfg.key or "interact"),
                    tonumber(revive_cfg.settle_ms) or POST_DIALOGUE_SETTLE_MS
                )
                logger(ctx).info(string.format(
                    "[Leveling] revive boss reentry interact fallback | source=%s objective_key=%s label=%s engage_window=%dms",
                    tostring(state.task_combat_locked_reentry_source or state.revive_reentry_source or ""),
                    tostring(state.task_combat_locked_objective_key or state.revive_reentry_objective_key or ""),
                    tostring(revive_cfg.label or revive_cfg.key or ""),
                    engage_ms
                ))
                return true
            end
            log_throttled(ctx, "revive_boss_reentry_interact_failed", "warn", LOG_THROTTLE_MS,
                "[Leveling] revive boss reentry interact failed: " .. tostring(err or "unknown error"))
        end

        if follow_task_path_to_anchor then
            if type(state.task_target) ~= "table" then
                if current_time >= (tonumber(state.next_task_button_click_at) or 0) then
                    state.stage = "revive_boss_reentry_call_task"
                    local clicked, click_err = M.click_main_task_button(ctx, current_time)
                    if not clicked then
                        log_throttled(ctx, "revive_boss_reentry_call_task_failed", "info", LOG_THROTTLE_MS,
                            "[Leveling] revive boss reentry waiting main task call near portal: " .. tostring(click_err or "not ready"))
                    end
                    return true
                end
                return false
            end
            state.stage = "revive_boss_reentry_follow_task"
            return false
        end

        return true
    end

    local map_cfg, map_name = current_map_task_config()
    if state.require_task_button_refresh == true or (tonumber(state.task_update_wait_until) or 0) > current_time then
        return false
    end
    if state.revive_reentry_pending == true then
        if normalize_map_name(state.revive_reentry_map_name) ~= normalize_map_name(map_name) then
            state.revive_reentry_pending = false
            state.revive_reentry_map_name = nil
        elseif type(map_cfg) == "table" and type(map_cfg.revive_reentry) == "table" then
            local revive_cfg = map_cfg.revive_reentry
            local anchor = type(revive_cfg.anchor) == "table" and revive_cfg.anchor or nil
            local anchor_x = tonumber(anchor and anchor.x)
            local anchor_y = tonumber(anchor and anchor.y)
            local anchor_z = tonumber(anchor and anchor.z)
            local interact_distance = math.max(120, tonumber(revive_cfg.interact_distance) or 240)
            local portal_max_distance = math.max(interact_distance * 2, tonumber(revive_cfg.portal_max_distance) or 1800)
            local best_portal = nil
            local portal_err = nil
            local nav_mod = nav_api(ctx)
            if type(nav_mod) == "table" and type(nav_mod.enum_portals) == "function" then
                local portals, enum_err = nav_mod.enum_portals()
                if type(portals) == "table" then
                    for _, item in ipairs(portals) do
                        local portal_x, portal_y, portal_z = extract_position_from_item(ctx, item)
                        if portal_x ~= nil and portal_y ~= nil then
                            local anchor_distance = anchor_x ~= nil and anchor_y ~= nil
                                and distance_2d(anchor_x, anchor_y, portal_x, portal_y)
                                or math.huge
                            if anchor_distance <= portal_max_distance then
                                local player_distance = distance_2d(player_x, player_y, portal_x, portal_y)
                                local score = anchor_distance + player_distance * 0.35
                                if not best_portal or score < (tonumber(best_portal.score) or math.huge) then
                                    best_portal = {
                                        x = portal_x,
                                        y = portal_y,
                                        z = portal_z,
                                        player_distance = player_distance,
                                        anchor_distance = anchor_distance,
                                        label = npc_label(item),
                                        score = score
                                    }
                                end
                            end
                        end
                    end
                else
                    portal_err = enum_err or "EnumPortal failed."
                end
            end

            local move_x = tonumber(best_portal and best_portal.x) or anchor_x
            local move_y = tonumber(best_portal and best_portal.y) or anchor_y
            local move_z = tonumber(best_portal and best_portal.z) or anchor_z
            if move_x ~= nil and move_y ~= nil then
                local move_distance = distance_2d(player_x, player_y, move_x, move_y)
                if move_distance > interact_distance then
                    state.stage = "revive_reentry_move"
                    issue_move(ctx, current_time, {
                        x = move_x,
                        y = move_y,
                        z = move_z,
                        source = "revive_reentry",
                        path_index = 0,
                        path_points = tonumber(state.task_path_count) or 0
                    })
                    log_throttled(ctx, "revive_reentry_move", "info", LOG_THROTTLE_MS, string.format(
                        "[Leveling] revive reentry moving to boss portal | map=%s target=%.2f, %.2f source=%s distance=%.2f",
                        tostring(map_name or ""),
                        tonumber(move_x) or 0,
                        tonumber(move_y) or 0,
                        best_portal and "portal" or "anchor",
                        tonumber(move_distance) or 0
                    ))
                    return true
                end
            end

            local state_key = map_transition_state_key(map_name, revive_cfg)
            local last_triggered_at = tonumber(state.map_transition_triggered[state_key]) or 0
            local retry_ms = math.max(800, tonumber(revive_cfg.retry_ms) or 1200)
            if last_triggered_at > 0 and current_time - last_triggered_at < retry_ms then
                return true
            end

            state.stage = "revive_reentry_interact"
            hold_navigation(ctx, current_time, "revive_reentry_interact")
            release_async_combat_inputs(ctx, current_time, true)
            local ok, err = press_keyboard_hotkey(ctx, current_time, VK_D, "leveling revive reentry")
            if ok then
                state.map_transition_triggered[state_key] = current_time
                state.revive_reentry_pending = false
                state.revive_reentry_map_name = nil
                schedule_task_refresh_after_transition(
                    ctx,
                    current_time,
                    "revive_reentry_" .. tostring(revive_cfg.key or "portal"),
                    tonumber(revive_cfg.settle_ms) or POST_DIALOGUE_SETTLE_MS
                )
                logger(ctx).info(string.format(
                    "[Leveling] revive reentry triggered | map=%s label=%s source=%s pos=%.2f, %.2f",
                    tostring(map_name or ""),
                    tostring(revive_cfg.label or revive_cfg.key or ""),
                    best_portal and "portal" or "anchor",
                    tonumber(move_x) or 0,
                    tonumber(move_y) or 0
                ))
                return true
            end

            log_throttled(ctx, "revive_reentry_failed", "warn", LOG_THROTTLE_MS,
                "[Leveling] revive reentry interact failed: " .. tostring(err or portal_err or "unknown error"))
            return true
        end
    end
    if type(map_cfg) ~= "table" or type(map_cfg.transitions) ~= "table" then
        return false
    end

    for _, transition in ipairs(map_cfg.transitions) do
        local trigger = type(transition) == "table" and transition.trigger or nil
        local step = type(transition) == "table" and transition.step or nil
        local trigger_x = tonumber(trigger and trigger.x)
        local trigger_y = tonumber(trigger and trigger.y)
        local trigger_radius = math.max(80, tonumber(trigger and trigger.radius) or 220)
        if trigger_x ~= nil and trigger_y ~= nil and type(step) == "table" then
            local trigger_distance = distance_2d(player_x, player_y, trigger_x, trigger_y)
            if trigger_distance <= trigger_radius then
                local state_key = map_transition_state_key(map_name, transition)
                local last_triggered_at = tonumber(state.map_transition_triggered[state_key]) or 0
                local retry_ms = math.max(1000, tonumber(transition.retry_ms) or 5000)
                if last_triggered_at <= 0 or current_time - last_triggered_at >= retry_ms then
                    local target, fetch_err = fetch_locator_button_target(ctx, step)
                    if target then
                        state.stage = "map_transition"
                        hold_navigation(ctx, current_time, "map_transition")
                        if try_click_map_transition(ctx, current_time, map_name, transition, target, trigger_distance) then
                            return true
                        end
                    else
                        log_throttled(ctx, "map_transition_missing", "info", LOG_THROTTLE_MS, string.format(
                            "[Leveling] map transition not visible yet | map=%s key=%s trigger_distance=%.2f err=%s",
                            tostring(map_name or ""),
                            tostring(transition.key or ""),
                            tonumber(trigger_distance) or 0,
                            tostring(fetch_err or "")
                        ))
                    end
                end
            end
        end
    end

    return false
end

schedule_task_refresh_after_transition = function(ctx, current_time, reason, wait_ms, opts)
    current_time = tonumber(current_time) or now_ms(ctx)
    opts = type(opts) == "table" and opts or {}
    local settle_ms = math.max(TASK_BUTTON_SETTLE_MS, tonumber(wait_ms) or POST_DIALOGUE_SETTLE_MS)
    state.task_update_wait_until = math.max(tonumber(state.task_update_wait_until) or 0, current_time + settle_ms)
    state.require_task_button_refresh = true
    state.pause_combat_until = math.max(tonumber(state.pause_combat_until) or 0, state.task_update_wait_until)
    state.next_task_button_click_at = math.max(
        tonumber(state.next_task_button_click_at) or 0,
        state.task_update_wait_until
    )
    state.next_task_refresh_at = math.max(
        tonumber(state.next_task_refresh_at) or 0,
        state.task_update_wait_until
    )
    state.next_follow_task_button_refresh_at = state.task_update_wait_until + TASK_BUTTON_KEEPALIVE_INTERVAL_MS
    state.next_task_button_soft_refresh_at = state.task_update_wait_until + TASK_BUTTON_SOFT_REFRESH_INTERVAL_MS
    local reason_text = tostring(reason or "")
    if reason_text == "global_task_portal"
        or reason_text == "exit_portal"
        or reason_text:find("map_transition_", 1, true) ~= nil
        or reason_text:find("revive_reentry_", 1, true) ~= nil
    then
        state.global_task_portal_guard_until = math.max(
            tonumber(state.global_task_portal_guard_until) or 0,
            state.task_update_wait_until + 4500
        )
        state.global_task_portal_guard_reason = reason_text
        state.global_task_portal_wait_reacquire = true
        state.global_task_portal_reacquire_reason = reason_text
    end
    clear_pending_interaction()
    clear_task_target_state()
    clear_runtime_objective_caches()
    if opts.force_task_call == true then
        local force_reason = "force_task_call_after_transition:" .. reason_text
        local reject_extra_ms = math.max(0, tonumber(opts.task_pos_reject_extra_ms) or 2500)
        state.require_task_button_refresh_reason = force_reason
        state.force_task_path_reacquire_until = math.max(
            tonumber(state.force_task_path_reacquire_until) or 0,
            current_time + math.max(18000, TASK_BUTTON_PATH_FETCH_TIMEOUT_MS * 3 + reject_extra_ms)
        )
        state.force_task_path_reacquire_reason = force_reason
        state.force_task_path_reacquire_extra_ms = reject_extra_ms
        state.task_pos_reject_until = math.max(
            tonumber(state.task_pos_reject_until) or 0,
            state.task_update_wait_until + TASK_BUTTON_PATH_FETCH_TIMEOUT_MS + reject_extra_ms
        )
        state.task_pos_reject_reason = force_reason
    end
    logger(ctx).info(string.format(
        "[Leveling] task refresh scheduled | reason=%s wait=%dms force_task_call=%s",
        tostring(reason or ""),
        settle_ms,
        opts.force_task_call == true and "true" or "false"
    ))
    return true
end

function M.global_task_portal_relevance(ctx, current_time, opts)
    opts = type(opts) == "table" and opts or {}
    local player_x = tonumber(opts.player_x)
    local player_y = tonumber(opts.player_y)
    local target = type(opts.target) == "table" and opts.target or state.task_target
    local destination = type(opts.destination) == "table" and opts.destination or nil
    local target_distance = nil
    if player_x ~= nil and player_y ~= nil and type(target) == "table" then
        local target_x = tonumber(target.x)
        local target_y = tonumber(target.y)
        if target_x ~= nil and target_y ~= nil then
            target_distance = distance_2d(player_x, player_y, target_x, target_y)
        end
    end

    local goal_distance = tonumber(opts.goal_distance)
    if goal_distance == nil and player_x ~= nil and player_y ~= nil and type(destination) == "table" then
        local destination_x = tonumber(destination.x)
        local destination_y = tonumber(destination.y)
        if destination_x ~= nil and destination_y ~= nil then
            goal_distance = distance_2d(player_x, player_y, destination_x, destination_y)
        end
    end

    local task_cfg = select(1, M.current_task_runtime_config())
    local max_target_distance = tonumber(type(task_cfg) == "table" and task_cfg.global_portal_max_target_distance)
        or 1100
    local max_goal_distance = tonumber(type(task_cfg) == "table" and task_cfg.global_portal_max_goal_distance)
        or 1800
    local allow_far_goal = type(task_cfg) == "table" and task_cfg.global_portal_allow_far_goal == true

    if target_distance ~= nil and target_distance > max_target_distance then
        return false, string.format(
            "target_distance %.2f > %.2f",
            tonumber(target_distance) or 0,
            tonumber(max_target_distance) or 0
            ), target_distance, goal_distance
    end
    if not allow_far_goal and goal_distance ~= nil and goal_distance > max_goal_distance then
        return false, string.format(
            "goal_distance %.2f > %.2f",
            tonumber(goal_distance) or 0,
            tonumber(max_goal_distance) or 0
        ), target_distance, goal_distance
    end

    return true, nil, target_distance, goal_distance
end

function M.maybe_click_global_task_portal(ctx, current_time, opts)
    current_time = tonumber(current_time) or now_ms(ctx)
    opts = type(opts) == "table" and opts or {}
    if state.revive_reentry_pending == true then
        return false
    end
    local stage_name = tostring(state.stage or "")
    local startup_resolving = (tonumber(state.startup_state_resolve_until) or 0) > current_time
    local reacquiring_task = startup_resolving
        or stage_name == "click_task_button"
        or stage_name == "wait_task_path_after_button"
        or stage_name == "wait_task"
        or stage_name == "wait_task_update"
        or stage_name == "refresh_task_button_after_dialogue"
        or stage_name == "loading"
        or (
            type(state.task_target) ~= "table"
            and stage_name ~= "task_reached"
            and stage_name ~= "task_combat"
            and stage_name ~= "task_combat_kite"
            and stage_name ~= "task_spawn_wait"
            and stage_name ~= "global_task_portal"
        )
    if reacquiring_task then
        log_throttled(ctx, "global_task_portal_reacquiring_task", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] global task portal suppressed during startup/task reacquire | stage=%s has_target=%s startup_resolving=%s",
            stage_name,
            type(state.task_target) == "table" and "true" or "false",
            startup_resolving and "true" or "false"
        ))
        return false
    end
    if state.global_task_portal_wait_reacquire == true then
        log_throttled(ctx, "global_task_portal_wait_reacquire", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] global task portal suppressed until task reacquired | reason=%s",
            tostring(state.global_task_portal_reacquire_reason or "")
        ))
        return false
    end
    if current_time < (tonumber(state.global_task_portal_guard_until) or 0) then
        log_throttled(ctx, "global_task_portal_guard", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] global task portal suppressed after transition | reason=%s remaining_ms=%d",
            tostring(state.global_task_portal_guard_reason or ""),
            math.max(0, (tonumber(state.global_task_portal_guard_until) or current_time) - current_time)
        ))
        return false
    end
    if state.require_task_button_refresh == true
        or current_time < (tonumber(state.task_update_wait_until) or 0)
        or (tonumber(state.dialogue_escape_due_at) or 0) > 0
    then
        return false
    end

    local portal_relevant, relevance_err, portal_target_distance, portal_goal_distance =
        M.global_task_portal_relevance(ctx, current_time, opts)
    if portal_relevant ~= true then
        log_throttled(ctx, "global_task_portal_irrelevant", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] global task portal suppressed; portal is not near current main task target | reason=%s stage=%s task=%s detail=%s target_distance=%s goal_distance=%s",
            tostring(relevance_err or ""),
            stage_name,
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(M.current_task_log_detail() or state.current_task_detail or ""),
            portal_target_distance ~= nil and string.format("%.2f", portal_target_distance) or "nil",
            portal_goal_distance ~= nil and string.format("%.2f", portal_goal_distance) or "nil"
        ))
        return false
    end
    if current_time < (tonumber(state.next_global_task_portal_scan_at) or 0) then
        return false
    end

    local scan_interval_ms = 900
    if state.stage == "follow_task" then
        local last_move_call_at = tonumber(state.last_move_call_at) or 0
        if last_move_call_at > 0 and current_time - last_move_call_at < 1400 then
            scan_interval_ms = 1700
        else
            scan_interval_ms = 1200
        end
    end
    state.next_global_task_portal_scan_at = current_time + scan_interval_ms

    local step = M.GLOBAL_TASK_PORTAL_STEP or {}
    local target, fetch_err = fetch_locator_button_target(ctx, step)
    if type(target) ~= "table" then
        if fetch_err then
            log_throttled(ctx, "global_task_portal_missing", "info", LOG_THROTTLE_MS,
                "[Leveling] global task portal button not visible: " .. tostring(fetch_err))
        end
        return false
    end

    if current_time < (tonumber(state.next_global_task_portal_click_at) or 0) then
        state.next_global_task_portal_scan_at = math.max(
            tonumber(state.next_global_task_portal_scan_at) or current_time,
            tonumber(state.next_global_task_portal_click_at) or current_time
        )
        return false
    end

    release_async_combat_inputs(ctx, current_time, true)
    local clicked, click_err, retryable = click_locator_button_target(ctx, step, target)
    if not clicked then
        log_throttled(ctx, retryable and "global_task_portal_click_retry" or "global_task_portal_click_failed",
            retryable and "info" or "warn", LOG_THROTTLE_MS,
            "[Leveling] global task portal click failed: " .. tostring(click_err))
        state.next_global_task_portal_click_at = current_time + 900
        return false
    end

    state.next_global_task_portal_click_at = current_time + 1800
    schedule_task_refresh_after_transition(ctx, current_time, "global_task_portal", POST_DIALOGUE_SETTLE_MS, {
        force_task_call = true,
        task_pos_reject_extra_ms = 3500
    })
    logger(ctx).info(string.format(
        "[Leveling] global task portal clicked | label=%s addr=%s pos=(%s,%s) stage=%s task=%s detail=%s player=(%s,%s) target_distance=%s goal_distance=%s name=%s related=%s",
        tostring(step.label or ""),
        tostring(target.addr or ""),
        tostring(target.x or ""),
        tostring(target.y or ""),
        stage_name,
        tostring(M.current_task_log_name() or state.current_task_name or ""),
        tostring(M.current_task_log_detail() or state.current_task_detail or ""),
        tostring(opts.player_x or ""),
        tostring(opts.player_y or ""),
        portal_target_distance ~= nil and string.format("%.2f", portal_target_distance) or "nil",
        portal_goal_distance ~= nil and string.format("%.2f", portal_goal_distance) or "nil",
        tostring(target.name or ""),
        tostring(target.related_text or "")
    ))
    return true
end

function M.maybe_handle_low_priority_task_ui(ctx, current_time, player_x, player_y, player_z, opts)
    opts = type(opts) == "table" and opts or {}
    opts.require_task_button_refresh = state.require_task_button_refresh == true
    opts.dialogue_escape_due = (tonumber(state.dialogue_escape_due_at) or 0) > 0
    opts.in_task_update_wait = current_time < (tonumber(state.task_update_wait_until) or 0)

    -- PortalBtn is a task-progression action, not a generic low-priority hint.
    -- Keep it on a throttled side path so follow_task can still click it without
    -- reopening the old "all low-priority UI can interrupt navigation" problem.
    if M.maybe_click_global_task_portal(ctx, current_time, {
        phase = tostring(opts.phase or ""),
        player_x = player_x,
        player_y = player_y,
        target = state.task_target,
        destination = build_task_destination_point(player_x, player_y),
        goal_distance = opts.goal_distance
    }) then
        state.stage = "global_task_portal"
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if type(M._leveling_policy) == "table"
        and type(M._leveling_policy.should_handle_low_priority_ui) == "function"
        and M._leveling_policy.should_handle_low_priority_ui(opts) ~= true
    then
        return false
    end

    if M.maybe_click_guide_skip(ctx, current_time) then
        state.stage = "guide_skip"
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    return false
end

try_click_exit_portal = function(ctx, current_time, portal_target, goal_distance, target_source)
    release_async_combat_inputs(ctx, current_time, true)

    local clicked, click_err, retryable = click_locator_button_target(ctx, EXIT_PORTAL_STEP, portal_target)
    if not clicked then
        if retryable then
            log_throttled(ctx, "exit_portal_click_retry", "warn", LOG_THROTTLE_MS,
                "[Leveling] exit portal click retry: " .. tostring(click_err))
            return false, click_err
        end
        log_throttled(ctx, "exit_portal_click_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] exit portal click failed: " .. tostring(click_err))
        return false, click_err
    end

    schedule_task_refresh_after_transition(ctx, current_time, "exit_portal", POST_DIALOGUE_SETTLE_MS, {
        force_task_call = true,
        task_pos_reject_extra_ms = 3500
    })
    logger(ctx).info(string.format(
        "[Leveling] exit portal clicked | label=%s goal_distance=%.2f target_source=%s pos=%.2f, %.2f",
        tostring(portal_target and (portal_target.related_text or portal_target.text or portal_target.name) or EXIT_PORTAL_STEP.label),
        tonumber(goal_distance) or 0,
        tostring(target_source or ""),
        tonumber(portal_target and portal_target.x) or 0,
        tonumber(portal_target and portal_target.y) or 0
    ))
    return true
end

local function enter_revive_state(ctx, current_time, hp)
    if (tonumber(state.revive_started_at) or 0) > 0 then
        return
    end

    state.revive_started_at = current_time
    state.revive_clicked_at = 0
    state.revive_click_count = 0
    state.revive_resume_ready_at = 0
    state.next_revive_click_at = current_time
    state.dialogue_escape_due_at = 0
    state.dialogue_confirm_deadline_at = 0
    state.next_dialogue_probe_at = 0
    state.dialogue_ui_confirmed = false
    state.dialogue_ui_match = nil
    state.task_update_wait_until = 0
    state.require_task_button_refresh = false
    clear_pending_interaction()
    state.last_move_call_at = 0
    state.next_move_at = 0
    state.stall_retry_count = 0
    state.pause_combat_until = math.max(tonumber(state.pause_combat_until) or 0, current_time + POST_UI_PAUSE_MS)
    clear_task_target_state()
    clear_runtime_objective_caches()
    state.post_revive_boss_engage_until = 0
    state.startup_state_resolve_until = 0
    logger(ctx).info(string.format(
        "[Leveling] player death detected | hp=%s enter revive flow.",
        hp ~= nil and string.format("%.2f", tonumber(hp) or 0) or "nil"
    ))
end

local function resume_after_revive(ctx, current_time, hp, player_x, player_y, player_z)
    reset_revive_state()
    clear_task_target_state()
    clear_runtime_objective_caches()
    clear_pending_interaction()
    M.clear_revive_reentry_state()
    state.require_task_button_refresh = false
    state.next_task_button_click_at = current_time
    state.next_task_refresh_at = current_time
    state.last_move_call_at = 0
    state.next_move_at = 0
    state.stall_retry_count = 0
    state.pause_combat_until = math.max(tonumber(state.pause_combat_until) or 0, current_time + POST_UI_PAUSE_MS)
    state.post_revive_boss_engage_until = 0
    state.startup_state_resolve_until = 0
    local map_cfg, map_name = current_map_task_config()
    local task_cfg, task_name = M.current_task_runtime_config()
    local objective_cfg = type(task_cfg) == "table" and type(task_cfg.objective) == "table" and task_cfg.objective or nil
    local revive_reentry_cfg, revive_reentry_source, revive_reentry_objective_key = M.resolve_revive_reentry_config()
    local boss_objective_active = (type(objective_cfg) == "table" and tostring(objective_cfg.mode or "") == "boss_kite")
        or state.task_combat_force_kite == true
        or type(revive_reentry_cfg) == "table"
    if boss_objective_active then
        local revive_boss_engage_ms = 10000
        state.post_revive_boss_engage_until = current_time + revive_boss_engage_ms
        logger(ctx).info(string.format(
            "[Leveling] revive boss combat armed | task=%s window=%dms",
            tostring(task_name or state.current_task_name or ""),
            revive_boss_engage_ms
        ))
    end
    local locked_point_cfg = M.find_objective_point_config_by_key(
        revive_reentry_objective_key or state.task_combat_locked_objective_key
    )
    local already_inside_locked_room = false
    if type(locked_point_cfg) == "table"
        and tonumber(locked_point_cfg.x) ~= nil
        and tonumber(locked_point_cfg.y) ~= nil
        and tonumber(locked_point_cfg.radius) ~= nil
    then
        local room_distance = distance_2d(
            player_x,
            player_y,
            tonumber(locked_point_cfg.x),
            tonumber(locked_point_cfg.y)
        )
        already_inside_locked_room = room_distance <= math.max(180, tonumber(locked_point_cfg.radius) or 0)
    end
    if type(revive_reentry_cfg) == "table" and not already_inside_locked_room then
        state.revive_reentry_pending = true
        state.revive_reentry_cfg = revive_reentry_cfg
        state.revive_reentry_source = revive_reentry_source
        state.revive_reentry_objective_key = revive_reentry_objective_key
        state.revive_reentry_deadline_at = current_time + math.max(
            8000,
            tonumber(revive_reentry_cfg.timeout_ms) or 18000
        )
        logger(ctx).info(string.format(
            "[Leveling] revive boss reentry armed | source=%s objective_key=%s label=%s timeout_ms=%d",
            tostring(revive_reentry_source or ""),
            tostring(revive_reentry_objective_key or ""),
            tostring(revive_reentry_cfg.label or revive_reentry_cfg.key or ""),
            math.max(8000, tonumber(revive_reentry_cfg.timeout_ms) or 18000)
        ))
    elseif type(map_cfg) == "table" and type(map_cfg.revive_reentry) == "table" then
        state.revive_reentry_pending = true
        state.revive_reentry_map_name = map_name
        logger(ctx).info(string.format(
            "[Leveling] revive map reentry armed | map=%s label=%s",
            tostring(map_name or ""),
            tostring(map_cfg.revive_reentry.label or map_cfg.revive_reentry.key or "")
        ))
    elseif type(revive_reentry_cfg) == "table" and already_inside_locked_room then
        logger(ctx).info(string.format(
            "[Leveling] revive boss reentry skipped | already inside boss room objective_key=%s pos=%.2f, %.2f",
            tostring(revive_reentry_objective_key or ""),
            tonumber(player_x) or 0,
            tonumber(player_y) or 0
        ))
    end
    logger(ctx).info(string.format(
        "[Leveling] revive completed | hp=%s pos=%.2f, %.2f, %.2f continue tasks.",
        hp ~= nil and string.format("%.2f", tonumber(hp) or 0) or "nil",
        tonumber(player_x) or 0,
        tonumber(player_y) or 0,
        tonumber(player_z) or 0
    ))
end

function M.should_defer_revive_for_task_entry_action(current_time, in_main_interface)
    local entry_action, task_name = M.current_task_entry_action_config()
    if type(entry_action) ~= "table"
        or tostring(entry_action.mode or "") ~= "world_map_send"
        or entry_action.defer_revive_during_map_entry ~= true
    then
        return false, nil, nil
    end

    if type(state.task_target) == "table" then
        return false, nil, nil
    end

    local armed_at = tonumber(state.task_entry_action_button_click_at) or 0
    local clicked_at = tonumber(state.last_task_button_click_at) or 0
    local started_at = armed_at > 0 and armed_at or clicked_at
    if started_at <= 0 then
        return false, nil, nil
    end

    local timeout_ms = math.max(5000, tonumber(entry_action.timeout_ms) or 12000)
    local elapsed_ms = current_time - started_at
    if elapsed_ms < 0 or elapsed_ms > timeout_ms then
        return false, nil, nil
    end

    -- Some map-selection panels can make player HP read as 0 while position is
    -- still available. Keep this opt-in and task-local so real combat deaths on
    -- ordinary tasks still enter revive immediately.
    if in_main_interface == false or armed_at > 0 then
        return true, entry_action, task_name
    end

    return false, nil, nil
end

local function maybe_handle_revive(ctx, current_time, hp, player_x, player_y, player_z, in_main_interface)
    local is_dead = type(hp) == "number" and hp <= 0
    local revive_active = (tonumber(state.revive_started_at) or 0) > 0
    if not is_dead and not revive_active then
        return false
    end

    if is_dead and not revive_active then
        local defer_revive, entry_action, task_name = M.should_defer_revive_for_task_entry_action(current_time, in_main_interface)
        if defer_revive then
            log_throttled(ctx, "revive_deferred_by_task_entry_action", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] hp=0 ignored during task entry action | task=%s key=%s stage=%s last_click_elapsed=%dms",
                tostring(task_name or state.current_task_name or ""),
                tostring(entry_action and entry_action.key or ""),
                tostring(state.stage or ""),
                math.max(0, current_time - (tonumber(state.task_entry_action_button_click_at) or tonumber(state.last_task_button_click_at) or current_time))
            ))
            return false
        end
    end

    if is_dead then
        enter_revive_state(ctx, current_time, hp)
    end

    state.stage = "revive"
    release_async_combat_inputs(ctx, current_time, true)
    hold_navigation(ctx, current_time, "revive")

    if not is_dead and player_x ~= nil and player_y ~= nil then
        if (tonumber(state.revive_resume_ready_at) or 0) == 0 then
            state.revive_resume_ready_at = current_time
        end

        local stable_elapsed = current_time - (tonumber(state.revive_resume_ready_at) or current_time)
        if stable_elapsed >= REVIVE_READY_STABLE_MS then
            resume_after_revive(ctx, current_time, hp, player_x, player_y, player_z)
        else
            log_throttled(ctx, "revive_wait_ready", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] revive waiting for player state to stabilize | stable=%d/%dms hp=%s",
                stable_elapsed,
                REVIVE_READY_STABLE_MS,
                hp ~= nil and string.format("%.2f", tonumber(hp) or 0) or "nil"
            ))
        end

        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    state.revive_resume_ready_at = 0
    if current_time >= (tonumber(state.next_revive_click_at) or 0) then
        try_click_revive_checkpoint(ctx, current_time)
    else
        log_throttled(ctx, "revive_wait_retry", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] waiting revive button retry | next_in=%dms clicks=%d",
            math.max(0, (tonumber(state.next_revive_click_at) or current_time) - current_time),
            tonumber(state.revive_click_count) or 0
        ))
    end

    log_heartbeat(ctx, current_time, player_x, player_y, player_z)
    return true
end

local function resolve_main_task_button_hint(ctx)
    local hint_x = tonumber(MAIN_TASK_BUTTON_STEP.hint_client_x)
    local hint_y = tonumber(MAIN_TASK_BUTTON_STEP.hint_client_y)
    if hint_x ~= nil and hint_y ~= nil then
        return hint_x, hint_y
    end

    local ratio_x = tonumber(MAIN_TASK_BUTTON_STEP.hint_ratio_x)
    local ratio_y = tonumber(MAIN_TASK_BUTTON_STEP.hint_ratio_y)
    if ratio_x == nil or ratio_y == nil then
        return nil, nil, "main task hint coordinates are unavailable."
    end

    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.window_hwnd) ~= "function" then
        return nil, nil, "nav.window_hwnd is unavailable."
    end

    local hwnd, hwnd_err = nav_mod.window_hwnd()
    if not hwnd then
        return nil, nil, hwnd_err or "game window not found."
    end

    local wnd_api = type(ctx) == "table" and ctx.wnd or wnd
    if type(wnd_api) ~= "table" or type(wnd_api.client_rect) ~= "function" then
        return nil, nil, "wnd.client_rect is unavailable."
    end

    local _, _, client_w, client_h = wnd_api.client_rect(hwnd)
    if type(client_w) ~= "number" or type(client_h) ~= "number" then
        return nil, nil, "wnd.client_rect failed."
    end

    return client_w * ratio_x, client_h * ratio_y
end

function M.refresh_current_task_name(ctx, current_time, button_target, hint_x, hint_y)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.enum_ui) ~= "function" then
        return state.current_task_name, "nav.enum_ui is unavailable."
    end

    local ui, ui_err = nav_mod.enum_ui()
    if type(ui) ~= "table" or type(ui.texts) ~= "table" then
        return state.current_task_name, tostring(ui_err or "enum_ui failed.")
    end

    local anchor_x = tonumber(button_target and button_target.x) or tonumber(hint_x)
    local anchor_y = tonumber(button_target and button_target.y) or tonumber(hint_y)
    if anchor_x == nil or anchor_y == nil then
        return state.current_task_name, "main task anchor unavailable."
    end

    local best_text, best_score = nil, nil
    for _, item in ipairs(ui.texts or {}) do
        local raw_text = trim(item and item.text or "")
        local text_x = tonumber(item and item.x)
        local text_y = tonumber(item and item.y)
        if raw_text ~= ""
            and text_x ~= nil
            and text_y ~= nil
        then
            local normalized = trim(raw_text:gsub("^涓荤嚎%s*", ""))
            local is_generic = normalized == ""
                or normalized == "涓荤嚎"
                or normalized == "鏀嚎"
                or normalized == "浠诲姟"
                or normalized == "鐩爣"
                or normalized == "杩借釜"
            local looks_numeric_only = normalized:match("^[%d%s%/%-%:%+]+$") ~= nil
            local dx = text_x - anchor_x
            local dy = math.abs(text_y - anchor_y)
            local in_task_band = dx >= -20 and dx <= 420 and dy <= 42
            if in_task_band and not is_generic and not looks_numeric_only then
                local score = 600 - dy * 8 - math.abs(dx - 88) * 1.6 - math.abs(#normalized - 12) * 0.8
                if best_score == nil or score > best_score then
                    best_score = score
                    best_text = normalized
                end
            end
        end
    end

    if best_text ~= nil and best_text ~= tostring(state.current_task_name or "") then
        state.current_task_name = best_text
        M.publish_current_task_name()
        logger(ctx).info(string.format(
            "[Leveling] current task updated | task=%s anchor=(%.2f,%.2f)",
            tostring(best_text),
            tonumber(anchor_x) or 0,
            tonumber(anchor_y) or 0
        ))
    elseif best_text ~= nil then
        state.current_task_name = best_text
        M.publish_current_task_name()
    end

    return state.current_task_name, best_text and nil or "main task text not found."
end

function M.refresh_current_task_name(ctx, current_time, button_target, hint_x, hint_y)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.enum_ui) ~= "function" then
        return state.current_task_name, "nav.enum_ui is unavailable."
    end

    local ui, ui_err = nav_mod.enum_ui()
    if type(ui) ~= "table" or type(ui.texts) ~= "table" then
        return state.current_task_name, tostring(ui_err or "enum_ui failed.")
    end

    local anchor_x = tonumber(button_target and button_target.x) or tonumber(hint_x)
    local anchor_y = tonumber(button_target and button_target.y) or tonumber(hint_y)
    if anchor_x == nil or anchor_y == nil then
        return state.current_task_name, "main task anchor unavailable."
    end

    local function normalize_task_title(value)
        local text = trim(value or "")
        if text == "" then
            return nil
        end
        text = trim(text:gsub("^涓荤嚎%s*", ""))
        text = trim(text:gsub("^涓荤窔%s*", ""))
        text = trim(text:gsub("^Main%s*Quest%s*[:锛?-]*%s*", ""))
        if text == "" then
            return nil
        end
        return text
    end

    local current_map_normalized = normalize_map_name(state.current_map_name)
    local task_panel = nil
    if type(nav_mod.get_task_panel_info) == "function" then
        task_panel = select(1, nav_mod.get_task_panel_info(ui))
    end

    local function normalize_panel_title(entry)
        if type(entry) ~= "table" then
            return nil
        end
        return normalize_task_title(entry.raw_text or entry.title)
    end

    local function select_task_panel_entry(preferred_task_name)
        if type(task_panel) ~= "table" or type(task_panel.tasks) ~= "table" then
            return nil
        end

        local preferred_key = normalize_map_name(preferred_task_name)
        local fallback_mainline = nil
        local fallback_any = nil

        for _, entry in ipairs(task_panel.tasks or {}) do
            local entry_title = normalize_panel_title(entry)
            local entry_key = normalize_map_name(entry_title)
            if fallback_any == nil then
                fallback_any = entry
            end
            if fallback_mainline == nil and tostring(entry.kind or ""):find("主线", 1, true) then
                fallback_mainline = entry
            end
            if preferred_key ~= nil and entry_key ~= nil then
                if entry_key == preferred_key
                    or preferred_key:find(entry_key, 1, true)
                    or entry_key:find(preferred_key, 1, true)
                then
                    return entry
                end
            end
        end

        if preferred_key == nil then
            return fallback_mainline or fallback_any
        end
        return nil
    end

    local function sync_task_detail_from_panel(preferred_task_name, source)
        local entry = select_task_panel_entry(preferred_task_name)
        if type(entry) == "table" then
            M.remember_task_panel_entry(entry, current_time)
        end
        local next_detail = trim(type(entry) == "table" and entry.detail or "")
        local previous_detail = trim(state.current_task_detail or "")
        if next_detail == previous_detail then
            if next_detail == "" and state.current_task_detail ~= nil then
                state.current_task_detail = nil
                state.current_task_detail_source = tostring(source or "task_panel")
                state.current_task_detail_updated_at = current_time
                M.publish_current_task_name()
            end
            return
        end
        state.current_task_detail = next_detail ~= "" and next_detail or nil
        state.current_task_detail_source = tostring(source or "task_panel")
        state.current_task_detail_updated_at = current_time
        M.publish_current_task_name()
        logger(ctx).info(string.format(
            "[Leveling] current task detail updated | task=%s detail=%s source=%s",
            tostring(state.current_task_name or preferred_task_name or ""),
            tostring(state.current_task_detail or ""),
            tostring(source or "task_panel")
        ))
    end

    local button_text_candidates = {
        normalize_task_title(button_target and button_target.text),
        normalize_task_title(button_target and button_target.related_text)
    }

    log_throttled(ctx, "task_name_button_candidates", "info", LOG_THROTTLE_MS, string.format(
        "[Leveling] task name candidates | map=%s button_text=%s related_text=%s anchor=(%.2f,%.2f)",
        tostring(state.current_map_name or ""),
        tostring(button_text_candidates[1] or ""),
        tostring(button_text_candidates[2] or ""),
        tonumber(anchor_x) or 0,
        tonumber(anchor_y) or 0
    ))

    for _, candidate in ipairs(button_text_candidates) do
        local candidate_key = normalize_map_name(candidate)
        if candidate ~= nil
            and candidate_key ~= nil
            and candidate_key ~= current_map_normalized
            and not M.is_known_map_name(candidate_key)
        then
            local preserve_current, preserve_reason = M.should_preserve_current_task_name(current_time, candidate, "button")
            if preserve_current == true then
                log_throttled(ctx, "task_name_preserve_button", "info", LOG_THROTTLE_MS, string.format(
                    "[Leveling] preserve current task name | source=button current=%s candidate=%s reason=%s",
                    tostring(state.current_task_name or ""),
                    tostring(candidate),
                    tostring(preserve_reason or "")
                ))
                goto continue_button_candidate
            end
            if candidate ~= tostring(state.current_task_name or "") then
                state.current_task_name = candidate
                state.current_task_name_source = "button"
                state.current_task_name_updated_at = current_time
                M.publish_current_task_name()
                logger(ctx).info(string.format(
                    "[Leveling] current task updated | task=%s source=button anchor=(%.2f,%.2f)",
                    tostring(candidate),
                    tonumber(anchor_x) or 0,
                    tonumber(anchor_y) or 0
                ))
            else
                state.current_task_name = candidate
                state.current_task_name_source = "button"
                state.current_task_name_updated_at = current_time
                M.publish_current_task_name()
            end
            sync_task_detail_from_panel(candidate, "button")
            return state.current_task_name, nil
        end
        ::continue_button_candidate::
    end

    local best_text, best_score = nil, nil
    local debug_candidates = {}
    for _, item in ipairs(ui.texts or {}) do
        local normalized = normalize_task_title(item and item.text or "")
        local text_x = tonumber(item and item.x)
        local text_y = tonumber(item and item.y)
        if normalized ~= nil
            and text_x ~= nil
            and text_y ~= nil
        then
            local normalized_key = normalize_map_name(normalized)
            local is_generic = normalized == ""
                or normalized == "涓荤嚎"
                or normalized == "涓荤窔"
                or normalized == "鏀嚎"
                or normalized == "浠诲嫏"
                or normalized == "浠诲姟"
                or normalized == "鐩爣"
                or normalized == "鐩"
                or normalized == "杩借釜"
                or normalized == "杩借工"
            local looks_numeric_only = normalized:match("^[%d%s%/%-%:%+]+$") ~= nil
            local looks_like_current_map = normalized_key ~= nil
                and current_map_normalized ~= nil
                and normalized_key == current_map_normalized
            local looks_like_known_map = normalized_key ~= nil and M.is_known_map_name(normalized_key)
            local dx = text_x - anchor_x
            local dy = math.abs(text_y - anchor_y)
            local in_task_band = dx >= -40 and dx <= 460 and dy <= 54
            if in_task_band and #debug_candidates < 6 then
                debug_candidates[#debug_candidates + 1] = string.format(
                    "text=%s dx=%.1f dy=%.1f generic=%s numeric=%s map_hit=%s known_map=%s",
                    tostring(normalized or ""),
                    tonumber(dx) or 0,
                    tonumber(dy) or 0,
                    is_generic and "true" or "false",
                    looks_numeric_only and "true" or "false",
                    looks_like_current_map and "true" or "false",
                    looks_like_known_map and "true" or "false"
                )
            end
            if in_task_band and not is_generic and not looks_numeric_only and not looks_like_current_map and not looks_like_known_map then
                local score = 600 - dy * 8 - math.abs(dx - 88) * 1.6 - math.abs(#normalized - 12) * 0.8
                if #normalized >= 6 then
                    score = score + 30
                end
                if best_score == nil or score > best_score then
                    best_score = score
                    best_text = normalized
                end
            end
        end
    end

    if best_text ~= nil and best_text ~= tostring(state.current_task_name or "") then
        local candidate_panel_entry = select_task_panel_entry(best_text)
        local current_panel_entry = select_task_panel_entry(state.current_task_name)
        local fallback_mainline_entry = current_panel_entry or select_task_panel_entry("主线")
        if type(candidate_panel_entry) ~= "table" and type(fallback_mainline_entry) == "table" then
            if type(current_panel_entry) ~= "table" then
                local panel_task_name = normalize_panel_title(fallback_mainline_entry)
                if panel_task_name ~= nil and panel_task_name ~= tostring(state.current_task_name or "") then
                    state.current_task_name = panel_task_name
                    state.current_task_name_source = "task_panel_guard"
                    state.current_task_name_updated_at = current_time
                    M.publish_current_task_name()
                    logger(ctx).info(string.format(
                        "[Leveling] current task updated | task=%s source=task_panel_guard panel_detail=%s",
                        tostring(panel_task_name),
                        tostring(fallback_mainline_entry.detail or "")
                    ))
                end
            end
            log_throttled(ctx, "task_name_nearby_unconfirmed", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] nearby task text not confirmed by task panel, keep panel task | current=%s candidate=%s panel=%s panel_detail=%s",
                tostring(state.current_task_name or ""),
                tostring(best_text or ""),
                tostring(fallback_mainline_entry.raw_text or fallback_mainline_entry.title or ""),
                tostring(fallback_mainline_entry.detail or "")
            ))
            sync_task_detail_from_panel(
                tostring(state.current_task_name or fallback_mainline_entry.raw_text or fallback_mainline_entry.title or ""),
                "task_panel_guard"
            )
            return state.current_task_name, nil
        end
        local preserve_source = type(candidate_panel_entry) == "table" and "task_panel_confirmed" or "nearby_text"
        local preserve_current, preserve_reason = M.should_preserve_current_task_name(current_time, best_text, preserve_source)
        if preserve_current == true then
            log_throttled(ctx, "task_name_preserve_nearby", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] preserve current task name | source=nearby_text current=%s candidate=%s reason=%s",
                tostring(state.current_task_name or ""),
                tostring(best_text),
                tostring(preserve_reason or "")
            ))
            return state.current_task_name, nil
        end
        if M.normalize_task_title_key(state.boss_soft_task_change_confirmed_candidate) == M.normalize_task_title_key(best_text)
            and (tonumber(state.boss_soft_task_change_confirmed_at) or 0) == current_time
        then
            logger(ctx).info(string.format(
                "[Leveling] boss nearby task change confirmed, release sticky | current=%s detail=%s candidate=%s elapsed=%dms count=%d",
                tostring(state.current_task_name or ""),
                tostring(state.current_task_detail or ""),
                tostring(best_text or ""),
                math.max(0, current_time - (tonumber(state.boss_soft_task_change_first_at) or current_time)),
                tonumber(state.boss_soft_task_change_seen_count) or 0
            ))
        end
        state.current_task_name = best_text
        state.current_task_name_source = preserve_source
        state.current_task_name_updated_at = current_time
        M.publish_current_task_name()
        logger(ctx).info(string.format(
            "[Leveling] current task updated | task=%s source=%s anchor=(%.2f,%.2f)",
            tostring(best_text),
            tostring(preserve_source),
            tonumber(anchor_x) or 0,
            tonumber(anchor_y) or 0
        ))
        sync_task_detail_from_panel(best_text, preserve_source)
    elseif best_text ~= nil then
        state.current_task_name = best_text
        M.publish_current_task_name()
        sync_task_detail_from_panel(best_text, "nearby_text")
    elseif state.current_task_name ~= nil then
        sync_task_detail_from_panel(state.current_task_name, "task_panel_fallback")
    end

    if best_text == nil and #debug_candidates > 0 then
        log_throttled(ctx, "task_name_nearby_candidates", "warn", LOG_THROTTLE_MS, string.format(
            "[Leveling] task name nearby_text candidates | %s",
            table.concat(debug_candidates, " | ")
        ))
    end

    return state.current_task_name, best_text and nil or "main task text not found."
end

function M.resolve_main_task_selected_target(nav_mod, hint_x, hint_y)
    if type(nav_mod) ~= "table" or type(nav_mod.get_current_selected_button) ~= "function" then
        return nil, "nav.get_current_selected_button is unavailable."
    end

    local selected, selected_err = nav_mod.get_current_selected_button()
    if type(selected) ~= "table" then
        return nil, selected_err or "Current selected button not found."
    end

    local addr = tonumber(selected.addr)
    local x = tonumber(selected.x)
    local y = tonumber(selected.y)
    if addr == nil or addr == 0 or x == nil or y == nil then
        return nil, "GetCurrentSelected returned TaskBtn without valid addr/coordinates."
    end

    local fullname = tostring(selected.Fullname or selected.fullname or "")
    local name = tostring(selected.name or "")
    local identity = (fullname .. " " .. name):lower()
    if identity:find("taskitem_c.widgettree.taskbtn", 1, true) == nil then
        return nil, "GetCurrentSelected is not main task TaskBtn."
    end

    local distance = 0
    if hint_x ~= nil and hint_y ~= nil then
        distance = distance_2d(hint_x, hint_y, x, y)
        if distance > math.max(tonumber(MAIN_TASK_BUTTON_STEP.hint_max_distance) or 80, 96) then
            return nil, string.format("GetCurrentSelected TaskBtn too far from hint: %.2f", tonumber(distance) or 0)
        end
    end

    return {
        kind = "button",
        addr = addr,
        name = name,
        text = tostring(selected.text or ""),
        fullname = fullname,
        x = x,
        y = y,
        related_text = tostring(selected.rel1_text or ""),
        distance = distance
    }
end

local function click_main_task_button(ctx, current_time, opts)
    opts = opts or {}
    local preserve_target = opts.preserve_target == true
    if M.should_suspend_treasure_task_refresh() then
        return false, "treasure dungeon suppresses main task refresh."
    end
    if current_time < (tonumber(state.next_task_button_click_at) or 0) then
        return false, "task button click cooldown."
    end
    release_async_combat_inputs(ctx, current_time, true)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.find_button_near_point) ~= "function" then
        return false, "nav.find_button_near_point is unavailable."
    end
    local ui_snapshot, ui_snapshot_err = nil, nil
    if type(nav_mod.enum_ui) == "function" then
        ui_snapshot, ui_snapshot_err = nav_mod.enum_ui()
        if type(ui_snapshot) ~= "table" then
            ui_snapshot = nil
        end
    end
    local ui_snapshot_summary = type(ui_snapshot) == "table"
        and string.format(
            "buttons=%d texts=%d images=%d",
            #(ui_snapshot.buttons or {}),
            #(ui_snapshot.texts or {}),
            #(ui_snapshot.images or {})
        )
        or tostring(ui_snapshot_err or "ui_snapshot_unavailable")
    local nav_debug_text = M.nav_debug_state_text(ctx)

    local function trace_main_task_call(phase, result, detail)
        local elapsed_ms = math.max(0, now_ms(ctx) - current_time)
        state.last_main_task_call_phase = tostring(phase or "")
        state.last_main_task_call_result = tostring(result or "")
        state.last_main_task_call_detail = tostring(detail or "")
        state.last_main_task_call_elapsed_ms = elapsed_ms
        state.last_main_task_call_nav = M.nav_debug_state_text(ctx)
        logger(ctx).info(string.format(
            "[Leveling] main task call trace | stage=%s phase=%s result=%s elapsed_ms=%d detail=%s",
            tostring(state.stage or ""),
            tostring(phase or ""),
            tostring(result or ""),
            elapsed_ms,
            tostring(detail or "")
        ))
    end

    local function format_main_task_click_target(target)
        if type(target) ~= "table" then
            return tostring(target or "")
        end

        local label = trim(target.text or target.related_text or target.anchor_text or "")
        if #label > 64 then
            label = label:sub(1, 61) .. "..."
        end
        local fullname = tostring(target.fullname or target.Fullname or "")
        if #fullname > 72 then
            fullname = fullname:sub(1, 69) .. "..."
        end

        return string.format(
            "kind=%s addr=%s pos=(%s,%s) text=%s name=%s fullname=%s distance=%s",
            tostring(target.kind or ""),
            tostring(target.addr or ""),
            tostring(target.x or ""),
            tostring(target.y or ""),
            label,
            tostring(target.name or ""),
            fullname,
            tostring(target.distance or "")
        )
    end

    local function log_main_task_panel_branch(source_prefix, branch, status, detail)
        logger(ctx).info(string.format(
            "[Leveling] main task panel branch | source=%s branch=%s status=%s detail=%s",
            tostring(source_prefix or ""),
            tostring(branch or ""),
            tostring(status or ""),
            tostring(detail or "")
        ))
    end

    local function attempt_main_task_control_click(phase, target, extra)
        if type(nav_mod.control_click) ~= "function" then
            return false, "nav.control_click is unavailable."
        end

        local click_target = type(target) == "table" and target or { addr = target }
        local addr = tonumber(click_target.addr)
        if addr == nil or addr == 0 then
            return false, "Invalid control address."
        end

        local click_started_at = now_ms(ctx)
        logger(ctx).info(string.format(
            "[Leveling] main task click dispatch | stage=%s phase=%s target=%s extra=%s",
            tostring(state.stage or ""),
            tostring(phase or ""),
            format_main_task_click_target(click_target),
            tostring(extra or "")
        ))
        local click_ok, click_err = nav_mod.control_click(addr)
        logger(ctx).info(string.format(
            "[Leveling] main task click result | stage=%s phase=%s ok=%s elapsed_ms=%d err=%s target=%s nav=%s",
            tostring(state.stage or ""),
            tostring(phase or ""),
            click_ok == true and "true" or "false",
            math.max(0, now_ms(ctx) - click_started_at),
            tostring(click_err or ""),
            format_main_task_click_target(click_target),
            M.nav_debug_state_text(ctx)
        ))
        return click_ok, click_err
    end

    local function apply_main_task_click_result(refresh_target, hint_x, hint_y)
        state.last_task_button_click_at = current_time
        state.next_follow_task_button_refresh_at = current_time + TASK_BUTTON_KEEPALIVE_INTERVAL_MS
        state.next_task_button_soft_refresh_at = current_time + TASK_BUTTON_SOFT_REFRESH_INTERVAL_MS
        state.next_task_refresh_at = current_time + TASK_BUTTON_SETTLE_MS
        M.refresh_current_task_name(ctx, current_time, refresh_target, hint_x, hint_y)

        if not preserve_target then
            clear_task_target_state()
            state.task_path_wait_until = current_time + TASK_BUTTON_PATH_FETCH_TIMEOUT_MS
            state.next_task_button_click_at = state.task_path_wait_until
            state.cached_nearest_npc = nil
            state.cached_npc_error = nil
            state.cached_task_monsters = nil
            state.cached_task_monster_error = nil
            state.pause_combat_until = math.max(tonumber(state.pause_combat_until) or 0, current_time + POST_UI_PAUSE_MS)
        else
            state.next_task_button_click_at = current_time + TASK_BUTTON_RETRY_INTERVAL_MS
            state.cached_nearest_npc = nil
            state.cached_npc_error = nil
            state.cached_task_monsters = nil
            state.cached_task_monster_error = nil
        end
    end

    local function find_main_task_control_near_point(client_x, client_y, max_distance)
        if type(nav_mod.find_controls_at_point) ~= "function" then
            return nil, "nav.find_controls_at_point is unavailable."
        end

        local controls, control_err = nav_mod.find_controls_at_point(client_x, client_y, {
            snapshot = ui_snapshot,
            include_buttons = true,
            include_images = true,
            include_texts = false,
            max_distance = max_distance,
            limit = 8
        })
        if type(controls) ~= "table" or #controls == 0 then
            return nil, control_err or "main task control not found."
        end

        local best = nil
        for _, control in ipairs(controls) do
            local addr = tonumber(control.addr)
            if addr ~= nil and addr ~= 0 then
                local fullname = tostring(control.fullname or "")
                local name = tostring(control.name or "")
                local identity = (fullname .. " " .. name):lower()
                local family_score = nil
                if identity:find("taskitem_c.widgettree.taskbtn", 1, true) ~= nil then
                    family_score = 0
                elseif identity:find("taskitem_c.widgettree.uiimage", 1, true) ~= nil then
                    family_score = 12
                elseif identity:find("taskitem_c.widgettree.fullimg", 1, true) ~= nil then
                    family_score = 18
                elseif identity:find("taskitem_c.widgettree.padding_dummy", 1, true) ~= nil then
                    family_score = 24
                elseif identity:find("taskitem_c.widgettree.shuangguang", 1, true) ~= nil then
                    family_score = 28
                elseif identity:find("taskitem_c.widgettree.", 1, true) ~= nil then
                    family_score = 36
                end

                if family_score ~= nil then
                    local candidate = {
                        kind = tostring(control.kind or ""),
                        addr = addr,
                        name = name,
                        text = tostring(control.text or ""),
                        fullname = fullname,
                        x = tonumber(control.x),
                        y = tonumber(control.y),
                        distance = tonumber(control.distance) or 0,
                        score = (tonumber(control.distance) or 0) + family_score
                    }
                    if best == nil or candidate.score < best.score then
                        best = candidate
                    end
                end
            end
        end

        if best ~= nil then
            return {
                kind = best.kind,
                addr = best.addr,
                name = best.name,
                text = best.text,
                fullname = best.fullname,
                x = best.x,
                y = best.y,
                distance = best.distance
            }
        end

        for _, control in ipairs(controls) do
            local addr = tonumber(control.addr)
            if addr ~= nil and addr ~= 0 then
                return {
                    kind = tostring(control.kind or ""),
                    addr = addr,
                    name = tostring(control.name or ""),
                    text = tostring(control.text or ""),
                    fullname = tostring(control.fullname or ""),
                    x = tonumber(control.x),
                    y = tonumber(control.y),
                    distance = tonumber(control.distance) or 0
                }
            end
        end

        return nil, "main task control address unavailable."
    end

    local function find_main_task_button_near_point(client_x, client_y, max_distance)
        if type(nav_mod.find_button_near_point) ~= "function" then
            return nil, "nav.find_button_near_point is unavailable."
        end

        return nav_mod.find_button_near_point(client_x, client_y, {
            snapshot = ui_snapshot,
            max_distance = max_distance
        })
    end

    local function collect_main_task_anchor_texts(panel_item)
        local values = {}
        local seen = {}

        local function push(value)
            local text = trim(value or "")
            if text == "" then
                return
            end
            local key = text:lower()
            if seen[key] then
                return
            end
            seen[key] = true
            values[#values + 1] = text
        end

        if type(panel_item) == "table" then
            push(panel_item.raw_text)
            push(panel_item.title)
        end
        if type(state.last_task_panel_entry) == "table" then
            push(state.last_task_panel_entry.raw_text)
            push(state.last_task_panel_entry.title)
        end

        local current_task_name = trim(M.current_task_log_name() or state.current_task_name or "")
        if current_task_name ~= "" then
            push(current_task_name)
            push("主线 " .. current_task_name)
        end

        return values
    end

    local function find_main_task_button_by_text_distance(anchor_texts, hint_x, hint_y)
        local function normalize_anchor_text(value)
            return trim(value or ""):lower()
        end

        local function find_main_task_button_by_text_geometry()
            if type(ui_snapshot) ~= "table"
                or type(ui_snapshot.buttons) ~= "table"
                or type(ui_snapshot.texts) ~= "table"
            then
                return nil, "main task text geometry snapshot unavailable."
            end

            local max_hint_distance = tonumber(MAIN_TASK_BUTTON_STEP.hint_max_distance) or 80
            local distance_min = tonumber(MAIN_TASK_BUTTON_STEP.distance_min) or 0
            local distance_max = tonumber(MAIN_TASK_BUTTON_STEP.distance_max) or distance_min
            if distance_max < distance_min then
                distance_min, distance_max = distance_max, distance_min
            end
            local target_distance = (distance_min + distance_max) * 0.5
            local best = nil

            for _, anchor_text in ipairs(anchor_texts or {}) do
                local anchor_key = normalize_anchor_text(anchor_text)
                if anchor_key ~= "" then
                    for _, text_item in ipairs(ui_snapshot.texts or {}) do
                        local text_value = trim(text_item and text_item.text or "")
                        local text_key = normalize_anchor_text(text_value)
                        local text_x = tonumber(text_item and text_item.x)
                        local text_y = tonumber(text_item and text_item.y)
                        if text_key == anchor_key
                            and text_x ~= nil
                            and text_y ~= nil
                        then
                            for _, button_item in ipairs(ui_snapshot.buttons or {}) do
                                local addr = tonumber(button_item and button_item.addr)
                                local button_x = tonumber(button_item and button_item.x)
                                local button_y = tonumber(button_item and button_item.y)
                                if addr ~= nil
                                    and addr ~= 0
                                    and button_x ~= nil
                                    and button_y ~= nil
                                then
                                    local hint_distance = hint_x ~= nil and hint_y ~= nil
                                        and distance_2d(hint_x, hint_y, button_x, button_y)
                                        or 0
                                    if hint_x == nil or hint_y == nil or hint_distance <= max_hint_distance then
                                        local anchor_distance = distance_2d(button_x, button_y, text_x, text_y)
                                        local dx = text_x - button_x
                                        local dy = math.abs(text_y - button_y)
                                        local in_distance_band = anchor_distance >= distance_min and anchor_distance <= distance_max
                                        local in_row_band = dx >= 8 and dx <= 84 and dy <= 24
                                        if in_distance_band and in_row_band then
                                            local fullname = tostring(button_item and (button_item.Fullname or button_item.fullname) or "")
                                            local name = tostring(button_item and button_item.name or "")
                                            local identity = (fullname .. " " .. name):lower()
                                            local identity_bonus = identity:find("taskitem_c.widgettree.taskbtn", 1, true) ~= nil and -16 or 0
                                            local score = math.abs(anchor_distance - target_distance)
                                                + dy * 2
                                                + math.abs(dx - 32) * 0.35
                                                + hint_distance * 0.08
                                                + identity_bonus
                                            if best == nil or score < best.score then
                                                best = {
                                                    kind = "button",
                                                    addr = addr,
                                                    name = name,
                                                    text = tostring(button_item and button_item.text or ""),
                                                    fullname = fullname,
                                                    x = button_x,
                                                    y = button_y,
                                                    distance = hint_distance,
                                                    related_text = text_value,
                                                    related_distance = anchor_distance,
                                                    score = score,
                                                    locator_mode = "text_geometry"
                                                }
                                            end
                                        end
                                    end
                                end
                            end
                        end
                    end
                end
            end

            if best ~= nil then
                return best
            end
            return nil, "main task text geometry matched no nearby button."
        end

        local errors = {}
        if type(nav_mod.find_button_by_locator) == "function" then
            for _, anchor_text in ipairs(anchor_texts or {}) do
                local locator = {
                    fullname = MAIN_TASK_BUTTON_STEP.include_patterns[1],
                    include_patterns = MAIN_TASK_BUTTON_STEP.include_patterns,
                    hint_client_x = hint_x,
                    hint_client_y = hint_y,
                    hint_max_distance = tonumber(MAIN_TASK_BUTTON_STEP.hint_max_distance) or 80,
                    distance_anchor_exact_text = anchor_text,
                    distance_button_name = tostring(MAIN_TASK_BUTTON_STEP.distance_button_name or ""),
                    distance_min = tonumber(MAIN_TASK_BUTTON_STEP.distance_min),
                    distance_max = tonumber(MAIN_TASK_BUTTON_STEP.distance_max)
                }
                local target, err = nav_mod.find_button_by_locator(locator, {
                    snapshot = ui_snapshot,
                    max_distance = tonumber(MAIN_TASK_BUTTON_STEP.hint_max_distance) or 80
                })
                if type(target) == "table" then
                    target.anchor_text = anchor_text
                    target.locator_mode = "identity_locator"
                    return target
                end
                if err ~= nil and err ~= "" then
                    errors[#errors + 1] = string.format("%s => %s", tostring(anchor_text), tostring(err))
                end
            end
        else
            errors[#errors + 1] = "nav.find_button_by_locator is unavailable."
        end

        local geometry_target, geometry_err = find_main_task_button_by_text_geometry()
        if type(geometry_target) == "table" then
            return geometry_target
        end
        if geometry_err ~= nil and geometry_err ~= "" then
            errors[#errors + 1] = geometry_err
        end

        return nil, #errors > 0
            and table.concat(errors, " | ")
            or "main task text-distance locator matched no button."
    end

    local function annotate_locator_click(panel_item, locator_button, source_prefix)
        if type(panel_item) ~= "table" or type(locator_button) ~= "table" then
            return panel_item
        end
        panel_item._main_task_click_source = tostring(source_prefix or "panel") .. "_locator"
        if tostring(locator_button.locator_mode or "") == "text_geometry" then
            panel_item._main_task_click_source = tostring(source_prefix or "panel") .. "_text_geometry"
        end
        panel_item._main_task_clicked_target = locator_button
        panel_item.button_addr = locator_button.addr
        panel_item.button_kind = "button"
        panel_item.button_name = tostring(locator_button.name or "")
        panel_item.button_fullname = tostring(locator_button.fullname or "")
        panel_item.button_x = tonumber(locator_button.x) or tonumber(panel_item.button_x) or tonumber(panel_item.x)
        panel_item.button_y = tonumber(locator_button.y) or tonumber(panel_item.button_y) or tonumber(panel_item.y)
        return panel_item
    end

    local function try_click_locator_button(panel_item, locator_button, source_prefix)
        if type(locator_button) ~= "table" or type(nav_mod.control_click) ~= "function" then
            return false, "main task locator button is invalid."
        end
        local clicked, click_err = attempt_main_task_control_click(
            tostring(source_prefix or "panel") .. "_locator",
            locator_button,
            "locator_mode=" .. tostring(locator_button.locator_mode or "")
        )
        if not clicked then
            return false, click_err or "main task locator control_click failed."
        end
        if type(panel_item) == "table" then
            return true, annotate_locator_click(panel_item, locator_button, source_prefix)
        end
        return true, locator_button
    end

    local function find_anchor_locator_target(hint_x, hint_y)
        local anchor_texts = collect_main_task_anchor_texts(nil)
        local target, err = find_main_task_button_by_text_distance(anchor_texts, hint_x, hint_y)
        if type(target) == "table" then
            return target
        end
        return nil, err
    end

    local function find_main_task_text_target(anchor_texts, hint_x, hint_y)
        if type(ui_snapshot) ~= "table" or type(ui_snapshot.texts) ~= "table" then
            return nil, "main task text snapshot unavailable."
        end

        local seen = {}
        local best = nil
        for _, anchor_text in ipairs(anchor_texts or {}) do
            local exact = trim(anchor_text or "")
            local key = exact:lower()
            if exact ~= "" and not seen[key] then
                seen[key] = true
                for _, item in ipairs(ui_snapshot.texts or {}) do
                    local raw_text = trim(item and item.text or "")
                    local addr = tonumber(item and item.addr)
                    local x = tonumber(item and item.x)
                    local y = tonumber(item and item.y)
                    if raw_text == exact and addr ~= nil and addr ~= 0 and x ~= nil and y ~= nil then
                        local hint_distance = hint_x ~= nil and hint_y ~= nil
                            and distance_2d(hint_x, hint_y, x, y)
                            or 0
                        local dx = hint_x ~= nil and (x - hint_x) or 0
                        local dy = hint_y ~= nil and math.abs(y - hint_y) or 0
                        local score = hint_distance + math.abs(dx - 32) * 0.35 + dy * 1.6
                        if best == nil or score < best.score then
                            best = {
                                kind = "text",
                                addr = addr,
                                name = tostring(item and item.name or ""),
                                text = raw_text,
                                fullname = tostring(item and (item.Fullname or item.fullname) or ""),
                                x = x,
                                y = y,
                                distance = hint_distance,
                                score = score
                            }
                        end
                    end
                end
            end
        end

        if best ~= nil then
            return best
        end
        return nil, "main task title text target not found."
    end

    local function promote_panel_click_to_selected_target(panel_item, source_tag, hint_x, hint_y, opts)
        opts = opts or {}
        local attempts = math.max(1, math.floor(tonumber(opts.attempts) or 2))
        local initial_wait_ms = math.max(0, math.floor(tonumber(opts.initial_wait_ms) or 90))
        local retry_wait_ms = math.max(0, math.floor(tonumber(opts.retry_wait_ms) or 70))
        logger(ctx).info(string.format(
            "[Leveling] main task selected promote begin | source=%s hint=(%.2f,%.2f) attempts=%d waits=%d/%d",
            tostring(source_tag or ""),
            tonumber(hint_x) or 0,
            tonumber(hint_y) or 0,
            attempts,
            initial_wait_ms,
            retry_wait_ms
        ))
        local selected_target, selected_err = nil, nil
        for attempt = 1, attempts do
            local attempt_started_at = now_ms(ctx)
            if attempt > 1 then
                sleep_ms(ctx, retry_wait_ms)
            elseif initial_wait_ms > 0 then
                sleep_ms(ctx, initial_wait_ms)
            end
            selected_target, selected_err = M.resolve_main_task_selected_target(nav_mod, hint_x, hint_y)
            logger(ctx).info(string.format(
                "[Leveling] main task selected promote probe | source=%s attempt=%d ok=%s elapsed_ms=%d detail=%s",
                tostring(source_tag or ""),
                attempt,
                type(selected_target) == "table" and "true" or "false",
                math.max(0, now_ms(ctx) - attempt_started_at),
                type(selected_target) == "table"
                    and format_main_task_click_target(selected_target)
                    or tostring(selected_err or "")
            ))
            if type(selected_target) == "table" then
                break
            end
        end
        if type(selected_target) ~= "table" then
            return false, selected_err or "GetCurrentSelected TaskBtn not available after panel click."
        end

        local click_ok, click_err = attempt_main_task_control_click(
            tostring(source_tag or "panel") .. "_selected_click",
            selected_target,
            "selected_promotion"
        )
        if not click_ok then
            return false, click_err or "GetCurrentSelected TaskBtn control_click failed."
        end

        panel_item._main_task_click_source = tostring(source_tag or "panel") .. "_selected"
        panel_item._main_task_clicked_target = selected_target
        panel_item.button_addr = selected_target.addr
        panel_item.button_kind = "button"
        panel_item.button_name = tostring(selected_target.name or "")
        panel_item.button_fullname = tostring(selected_target.fullname or "")
        panel_item.button_x = tonumber(selected_target.x) or tonumber(panel_item.button_x) or tonumber(panel_item.x)
        panel_item.button_y = tonumber(selected_target.y) or tonumber(panel_item.button_y) or tonumber(panel_item.y)
        return true, panel_item
    end

    local function click_anchor_button_target(target, phase)
        if type(target) ~= "table" then
            return false, "main task target is invalid."
        end

        local identity = table.concat({
            tostring(target.kind or ""),
            tostring(target.name or ""),
            tostring(target.fullname or "")
        }, " "):lower()
        local is_button = tostring(target.kind or "") == "button"
            or identity:find("taskitem_c.widgettree.taskbtn", 1, true) ~= nil
        if not is_button then
            return false, "main task target is not a real button."
        end

        local click_ok, click_err = attempt_main_task_control_click(
            phase or "anchor_button",
            target,
            "final_anchor_target"
        )
        if not click_ok then
            return false, click_err
        end

        return true, target
    end

    local function try_click_main_task_panel_item(panel_item, source_prefix, hint_x, hint_y)
        if type(panel_item) ~= "table" then
            return false, "task panel entry is invalid."
        end
        local button_x = tonumber(panel_item.button_x) or tonumber(panel_item.x)
        local button_y = tonumber(panel_item.button_y) or tonumber(panel_item.y)
        local addr = tonumber(panel_item.button_addr)
        local button_kind = tostring(panel_item.button_kind or "")
        local button_fullname = tostring(panel_item.button_fullname or ""):lower()
        local prefer_direct_addr = button_kind == "button"
            or button_fullname:find("taskitem_c.widgettree.taskbtn", 1, true) ~= nil
        local last_err = nil
        local branch_errors = {}

        local function record_branch_error(branch, err)
            local detail = tostring(err or "")
            if detail == "" then
                return
            end
            last_err = detail
            branch_errors[#branch_errors + 1] = string.format("%s=%s", tostring(branch or ""), detail)
            log_main_task_panel_branch(source_prefix, branch, "miss", detail)
        end

        local function record_branch_hit(branch, detail)
            log_main_task_panel_branch(source_prefix, branch, "hit", detail)
        end

        log_main_task_panel_branch(source_prefix, "entry", "begin", string.format(
            "raw=%s kind=%s button_kind=%s button_addr=%s title_addr=%s button_pos=(%s,%s) hint=(%s,%s) prefer_direct=%s",
            tostring(panel_item.raw_text or panel_item.title or ""),
            tostring(panel_item.kind or ""),
            button_kind,
            tostring(addr or ""),
            tostring(panel_item.title_addr or ""),
            tostring(button_x or ""),
            tostring(button_y or ""),
            tostring(hint_x or ""),
            tostring(hint_y or ""),
            prefer_direct_addr == true and "true" or "false"
        ))

        if prefer_direct_addr and addr ~= nil and addr ~= 0 and type(nav_mod.control_click) == "function" then
            local clicked, click_err = attempt_main_task_control_click(
                tostring(source_prefix or "panel") .. "_addr",
                {
                    kind = panel_item.button_kind or panel_item.kind,
                    addr = addr,
                    x = button_x,
                    y = button_y,
                    text = panel_item.raw_text or panel_item.title or "",
                    name = panel_item.button_name or "",
                    fullname = panel_item.button_fullname or ""
                },
                "panel_direct_addr"
            )
            if clicked then
                panel_item._main_task_click_source = tostring(source_prefix or "panel") .. "_addr"
                record_branch_hit("direct_addr", format_main_task_click_target({
                    kind = panel_item.button_kind or panel_item.kind,
                    addr = addr,
                    x = button_x,
                    y = button_y,
                    text = panel_item.raw_text or panel_item.title or "",
                    name = panel_item.button_name or "",
                    fullname = panel_item.button_fullname or ""
                }))
                return true, panel_item
            end
            record_branch_error("direct_addr", click_err or "task panel control_click failed.")
        elseif addr == nil or addr == 0 then
            record_branch_error("direct_addr", "task panel entry button address unavailable.")
        else
            record_branch_error("direct_addr", "task panel entry anchor is not a direct button.")
        end

        local anchor_texts = collect_main_task_anchor_texts(panel_item)
        if #anchor_texts > 0 then
            local locator_button, locator_err = find_main_task_button_by_text_distance(anchor_texts, hint_x, hint_y)
            if type(locator_button) == "table" then
                local clicked, clicked_target_or_err = try_click_locator_button(panel_item, locator_button, source_prefix)
                if clicked then
                    record_branch_hit("locator", format_main_task_click_target(locator_button))
                    return true, clicked_target_or_err
                end
                record_branch_error("locator", clicked_target_or_err or "task panel text-distance button control_click failed.")
            elseif locator_err ~= nil and locator_err ~= "" then
                record_branch_error("locator", locator_err)
            end
        end

        local selected_hint_x = tonumber(panel_item.button_x) or button_x or hint_x
        local selected_hint_y = tonumber(panel_item.button_y) or button_y or hint_y
        local title_addr = tonumber(panel_item.title_addr)
        if title_addr ~= nil and title_addr ~= 0 and type(nav_mod.control_click) == "function" then
            local clicked, click_err = attempt_main_task_control_click(
                tostring(source_prefix or "panel") .. "_title_probe",
                {
                    kind = "text",
                    addr = title_addr,
                    x = tonumber(panel_item.x),
                    y = tonumber(panel_item.y),
                    text = panel_item.title or panel_item.raw_text or "",
                    name = "",
                    fullname = ""
                },
                "panel_title_probe"
            )
            if clicked then
                local promoted_ok, promoted_item_or_err = promote_panel_click_to_selected_target(
                    panel_item,
                    tostring(source_prefix or "panel") .. "_title",
                    selected_hint_x,
                    selected_hint_y,
                    {
                        attempts = 2,
                        initial_wait_ms = 90,
                        retry_wait_ms = 70
                    }
                )
                if promoted_ok then
                    record_branch_hit("title_selected", format_main_task_click_target(
                        type(panel_item._main_task_clicked_target) == "table" and panel_item._main_task_clicked_target or {
                            kind = "text",
                            addr = title_addr,
                            x = tonumber(panel_item.x),
                            y = tonumber(panel_item.y),
                            text = panel_item.title or panel_item.raw_text or ""
                        }
                    ))
                    return true, panel_item
                end
                record_branch_error("title_selected", promoted_item_or_err or "task panel title click did not promote to TaskBtn.")
            else
                record_branch_error("title_click", click_err or "task panel title control_click failed.")
            end
        end

        if not prefer_direct_addr
            and addr ~= nil
            and addr ~= 0
            and type(nav_mod.control_click) == "function"
        then
            local clicked, click_err = attempt_main_task_control_click(
                tostring(source_prefix or "panel") .. "_anchor_addr_probe",
                {
                    kind = panel_item.button_kind or panel_item.kind,
                    addr = addr,
                    x = button_x,
                    y = button_y,
                    text = panel_item.raw_text or panel_item.title or "",
                    name = panel_item.button_name or "",
                    fullname = panel_item.button_fullname or ""
                },
                "panel_anchor_probe"
            )
            if clicked then
                local promoted_ok, promoted_item_or_err = promote_panel_click_to_selected_target(
                    panel_item,
                    tostring(source_prefix or "panel") .. "_anchor_addr",
                    selected_hint_x,
                    selected_hint_y,
                    {
                        attempts = 2,
                        initial_wait_ms = 90,
                        retry_wait_ms = 70
                    }
                )
                if promoted_ok then
                    record_branch_hit("anchor_addr_selected", format_main_task_click_target(
                        type(panel_item._main_task_clicked_target) == "table" and panel_item._main_task_clicked_target or {
                            kind = panel_item.button_kind or panel_item.kind,
                            addr = addr,
                            x = button_x,
                            y = button_y,
                            text = panel_item.raw_text or panel_item.title or ""
                        }
                    ))
                    return true, panel_item
                end
                record_branch_error("anchor_addr_selected", promoted_item_or_err or "task panel anchor did not promote to TaskBtn.")
            else
                record_branch_error("anchor_addr_click", click_err or "task panel anchor control_click failed.")
            end
        end

        if button_x ~= nil and button_y ~= nil then
            local nearby_button, nearby_button_err = find_main_task_button_near_point(
                button_x,
                button_y,
                math.max(tonumber(MAIN_TASK_BUTTON_STEP.hint_max_distance) or 80, 28)
            )
            if type(nearby_button) == "table" and type(nav_mod.control_click) == "function" then
                local clicked, click_err = attempt_main_task_control_click(
                    tostring(source_prefix or "panel") .. "_nearby_button",
                    nearby_button,
                    "panel_nearby_button"
                )
                if clicked then
                    panel_item._main_task_click_source = tostring(source_prefix or "panel") .. "_nearby_button"
                    panel_item._main_task_clicked_target = nearby_button
                    panel_item.button_addr = nearby_button.addr
                    panel_item.button_kind = "button"
                    panel_item.button_name = tostring(nearby_button.name or "")
                    panel_item.button_fullname = tostring(nearby_button.fullname or "")
                    panel_item.button_x = tonumber(nearby_button.x) or button_x
                    panel_item.button_y = tonumber(nearby_button.y) or button_y
                    record_branch_hit("nearby_button", format_main_task_click_target(nearby_button))
                    return true, panel_item
                end
                record_branch_error("nearby_button", click_err or "task panel nearby button control_click failed.")
            elseif nearby_button_err ~= nil and nearby_button_err ~= "" then
                record_branch_error("nearby_button", nearby_button_err)
            end
        end

        return false, (#branch_errors > 0 and table.concat(branch_errors, " | ")) or last_err or "task panel entry click failed.", panel_item
    end

    local function build_main_task_panel_queries()
        local queries = {}
        local seen = {}
        local function push(value)
            local text = trim(value or "")
            if text == "" or seen[text] then
                return
            end
            seen[text] = true
            queries[#queries + 1] = text
        end
        push(M.current_task_log_name())
        push(state.current_task_name)
        push(M.current_task_log_detail())
        push(state.current_task_detail)
        push(_G.AVEPOINT_LAST_TASK_NAME)
        push(_G.AVEPOINT_LAST_TASK_DETAIL)
        if type(state.last_task_panel_entry) == "table" then
            push(state.last_task_panel_entry.title)
            push(state.last_task_panel_entry.raw_text)
            push(state.last_task_panel_entry.detail)
        end
        return queries
    end

    local panel_queries = build_main_task_panel_queries()
    state.last_main_task_call_started_at = current_time
    state.last_main_task_call_stage = tostring(state.stage or "")
    state.last_main_task_call_queries = #panel_queries > 0 and table.concat(panel_queries, " -> ") or ""
    state.last_main_task_call_phase = "begin"
    state.last_main_task_call_result = "running"
    state.last_main_task_call_detail = ""
    state.last_main_task_call_elapsed_ms = 0
    state.last_main_task_call_nav = nav_debug_text
    state.last_main_task_call_ui = ui_snapshot_summary
    logger(ctx).info(string.format(
        "[Leveling] main task call begin | stage=%s task=%s detail=%s mode=%s queries=%s ui=%s nav=%s",
        tostring(state.stage or ""),
        tostring(M.current_task_log_name() or state.current_task_name or ""),
        tostring(M.current_task_log_detail() or state.current_task_detail or ""),
        preserve_target == true and "soft_refresh" or "hard_refresh",
        state.last_main_task_call_queries,
        ui_snapshot_summary,
        nav_debug_text
    ))

    local hint_x, hint_y, hint_err = resolve_main_task_button_hint(ctx)
    if hint_x == nil or hint_y == nil then
        state.next_task_button_click_at = current_time + TASK_BUTTON_RETRY_INTERVAL_MS
        log_throttled(ctx, "task_button_hint_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] main task button hint failed: " .. tostring(hint_err))
        return false, hint_err
    end

    local selected_target, selected_err = M.resolve_main_task_selected_target(nav_mod, hint_x, hint_y)
    if type(selected_target) == "table" then
        local click_ok, click_err = attempt_main_task_control_click(
            "current_selected_click",
            selected_target,
            "current_selected"
        )
        if click_ok then
            apply_main_task_click_result(selected_target, hint_x, hint_y)
            trace_main_task_call("current_selected", "success", string.format(
                "addr=%s pos=(%s,%s)",
                tostring(selected_target.addr or ""),
                tostring(selected_target.x or ""),
                tostring(selected_target.y or "")
            ))
            logger(ctx).info(string.format(
                "[Leveling] main task button clicked | task=%s mode=%s source=%s label=%s kind=%s addr=%s pos=(%s,%s) hint=(%.2f,%.2f) distance=%.2f",
                tostring(M.current_task_log_name() or ""),
                preserve_target == true and "soft_refresh" or "hard_refresh",
                "current_selected",
                tostring(MAIN_TASK_BUTTON_STEP.label),
                tostring(selected_target.kind or ""),
                tostring(selected_target.addr or ""),
                tostring(selected_target.x or ""),
                tostring(selected_target.y or ""),
                tonumber(hint_x) or 0,
                tonumber(hint_y) or 0,
                tonumber(selected_target.distance) or 0
            ))
            return true
        end
        selected_err = click_err or "GetCurrentSelected control_click failed."
        trace_main_task_call("current_selected", "click_failed", selected_err)
    else
        trace_main_task_call("current_selected", "miss", selected_err or "Current selected button not found.")
    end

    local preferred_panel_key = M.current_task_log_name()
        or state.current_task_name
        or M.current_task_log_detail()
        or state.current_task_detail

    local cached_panel_item = M.get_cached_task_panel_entry(current_time, preferred_panel_key)
    if type(cached_panel_item) == "table" then
        local cached_ok, cached_item = try_click_main_task_panel_item(cached_panel_item, "panel_cache", hint_x, hint_y)
        if cached_ok and type(cached_item) == "table" then
            local refresh_target = {
                x = tonumber(cached_item.button_x) or tonumber(cached_item.x),
                y = tonumber(cached_item.button_y) or tonumber(cached_item.y),
                text = tostring(cached_item.raw_text or cached_item.title or ""),
                related_text = tostring(cached_item.title or "")
            }
            apply_main_task_click_result(
                refresh_target,
                tonumber(cached_item.button_x) or tonumber(cached_item.x),
                tonumber(cached_item.button_y) or tonumber(cached_item.y)
            )
            trace_main_task_call(
                tostring(cached_item._main_task_click_source or "panel_cache"),
                "success",
                tostring(cached_item.raw_text or cached_item.title or "")
            )
            logger(ctx).info(string.format(
                "[Leveling] main task panel entry clicked | query=%s task=%s detail=%s mode=%s source=%s raw=%s kind=%s addr=%s pos=(%.2f,%.2f) resolved_kind=%s resolved_addr=%s",
                tostring(preferred_panel_key or ""),
                tostring(M.current_task_log_name() or ""),
                tostring(M.current_task_log_detail() or ""),
                preserve_target == true and "soft_refresh" or "hard_refresh",
                tostring(cached_item._main_task_click_source or ""),
                tostring(cached_item.raw_text or cached_item.title or ""),
                tostring(cached_item.kind or ""),
                tostring(cached_item.button_addr or ""),
                tonumber(refresh_target.x) or 0,
                tonumber(refresh_target.y) or 0,
                tostring(type(cached_item._main_task_clicked_target) == "table" and cached_item._main_task_clicked_target.kind or ""),
                tostring(type(cached_item._main_task_clicked_target) == "table" and cached_item._main_task_clicked_target.addr or "")
            ))
            return true
        end
        trace_main_task_call("panel_cache", "miss", tostring(cached_item or "cached panel click failed"))
    end

    if type(nav_mod.find_task_panel_entry) == "function" then
        local panel_errors = {}
        for _, query in ipairs(panel_queries) do
            local panel_item, panel_err = nav_mod.find_task_panel_entry(query, ui_snapshot, {
                exact = false
            })
            if type(panel_item) == "table" then
                M.remember_task_panel_entry(panel_item, current_time)
            end
            local panel_ok, clicked_item, panel_meta = try_click_main_task_panel_item(panel_item, "panel", hint_x, hint_y)
            if panel_ok and type(clicked_item) == "table" then
                local refresh_target = {
                    x = tonumber(clicked_item.button_x) or tonumber(clicked_item.x),
                    y = tonumber(clicked_item.button_y) or tonumber(clicked_item.y),
                    text = tostring(clicked_item.raw_text or clicked_item.title or ""),
                    related_text = tostring(clicked_item.title or "")
                }
                apply_main_task_click_result(
                    refresh_target,
                    tonumber(clicked_item.button_x) or tonumber(clicked_item.x),
                    tonumber(clicked_item.button_y) or tonumber(clicked_item.y)
                )
                trace_main_task_call(
                    tostring(clicked_item._main_task_click_source or "panel"),
                    "success",
                    string.format("query=%s raw=%s", tostring(query), tostring(clicked_item.raw_text or clicked_item.title or ""))
                )

                logger(ctx).info(string.format(
                    "[Leveling] main task panel entry clicked | query=%s task=%s detail=%s mode=%s source=%s raw=%s kind=%s addr=%s pos=(%.2f,%.2f) resolved_kind=%s resolved_addr=%s",
                    tostring(query),
                    tostring(M.current_task_log_name() or ""),
                    tostring(M.current_task_log_detail() or ""),
                    preserve_target == true and "soft_refresh" or "hard_refresh",
                    tostring(clicked_item._main_task_click_source or ""),
                    tostring(clicked_item.raw_text or clicked_item.title or ""),
                    tostring(clicked_item.kind or ""),
                    tostring(clicked_item.button_addr or ""),
                    tonumber(refresh_target.x) or 0,
                    tonumber(refresh_target.y) or 0,
                    tostring(type(clicked_item._main_task_clicked_target) == "table" and clicked_item._main_task_clicked_target.kind or ""),
                    tostring(type(clicked_item._main_task_clicked_target) == "table" and clicked_item._main_task_clicked_target.addr or "")
                ))
                return true
            end
            local panel_error_text = tostring(panel_err or clicked_item or panel_meta or "")
            if panel_error_text ~= "" then
                trace_main_task_call("panel_query", "miss", string.format("%s => %s", tostring(query), panel_error_text))
                panel_errors[#panel_errors + 1] = string.format("%s => %s", tostring(query), panel_error_text)
            end
        end
        logger(ctx).info(string.format(
            "[Leveling] main task panel queries missed, fallback to anchor button | stage=%s task=%s detail=%s errors=%s",
            tostring(state.stage or ""),
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(M.current_task_log_detail() or state.current_task_detail or ""),
            #panel_errors > 0 and table.concat(panel_errors, " | ") or ""
        ))
    end

    local target, fetch_err = nav_mod.find_button_near_point(hint_x, hint_y, {
        snapshot = ui_snapshot,
        include_patterns = MAIN_TASK_BUTTON_STEP.include_patterns,
        max_distance = tonumber(MAIN_TASK_BUTTON_STEP.hint_max_distance) or 80
    })
    local target_source = "anchor_button"
    if not target then
        local locator_target, locator_err = find_anchor_locator_target(hint_x, hint_y)
        if type(locator_target) == "table" then
            target = locator_target
            target_source = tostring(locator_target.locator_mode or "") == "text_geometry"
                and "anchor_text_geometry"
                or "anchor_locator"
            trace_main_task_call(target_source, "matched", string.format(
                "addr=%s pos=(%s,%s)",
                tostring(locator_target.addr or ""),
                tostring(locator_target.x or ""),
                tostring(locator_target.y or "")
            ))
        elseif locator_err ~= nil and locator_err ~= "" then
            fetch_err = locator_err
            trace_main_task_call("anchor_locator", "miss", locator_err)
        end
    end
    if not target then
        local title_target, title_err = find_main_task_text_target(collect_main_task_anchor_texts(nil), hint_x, hint_y)
        if type(title_target) == "table" then
            local clicked, click_err = attempt_main_task_control_click(
                "anchor_title_probe",
                title_target,
                "anchor_title_probe"
            )
            if clicked then
                local promoted_item = {
                    title_addr = title_target.addr,
                    x = title_target.x,
                    y = title_target.y,
                    button_x = hint_x,
                    button_y = hint_y
                }
                local promoted_ok, promoted_item_or_err = promote_panel_click_to_selected_target(
                    promoted_item,
                    "anchor_title",
                    hint_x,
                    hint_y,
                    {
                        attempts = 2,
                        initial_wait_ms = 90,
                        retry_wait_ms = 70
                    }
                )
                if promoted_ok then
                    target = type(promoted_item._main_task_clicked_target) == "table"
                        and promoted_item._main_task_clicked_target
                        or target
                    target_source = "anchor_title_selected"
                    trace_main_task_call("anchor_title_selected", "matched", string.format(
                        "title=%s addr=%s",
                        tostring(title_target.text or ""),
                        tostring(type(promoted_item._main_task_clicked_target) == "table" and promoted_item._main_task_clicked_target.addr or "")
                    ))
                else
                    fetch_err = promoted_item_or_err or "main task title target did not promote to TaskBtn."
                    trace_main_task_call("anchor_title_selected", "miss", fetch_err)
                end
            else
                fetch_err = click_err or "main task title target control_click failed."
                trace_main_task_call("anchor_title_click", "click_failed", fetch_err)
            end
        elseif title_err ~= nil and title_err ~= "" then
            fetch_err = title_err
            trace_main_task_call("anchor_title", "miss", title_err)
        end
    end
    if not target then
        local nearby_button, nearby_button_err = find_main_task_button_near_point(
            hint_x,
            hint_y,
            math.max(tonumber(MAIN_TASK_BUTTON_STEP.hint_max_distance) or 80, 28)
        )
        if type(nearby_button) == "table" then
            target = nearby_button
            target_source = "anchor_button_any"
            trace_main_task_call("anchor_button_any", "matched", string.format(
                "addr=%s pos=(%s,%s)",
                tostring(nearby_button.addr or ""),
                tostring(nearby_button.x or ""),
                tostring(nearby_button.y or "")
            ))
        elseif nearby_button_err ~= nil and nearby_button_err ~= "" then
            fetch_err = nearby_button_err
            trace_main_task_call("anchor_button_any", "miss", nearby_button_err)
        end
    end
    if not target then
        local nearby_detail = ""
        if type(nav_mod.find_controls_at_point) == "function" then
            local controls = select(1, nav_mod.find_controls_at_point(hint_x, hint_y, {
                snapshot = ui_snapshot,
                include_buttons = true,
                include_images = true,
                include_texts = true,
                max_distance = 140,
                limit = 5
            }))
            if type(controls) == "table" and #controls > 0 then
                local parts = {}
                for _, control in ipairs(controls) do
                    local label = tostring(control.fullname or control.name or control.kind or "")
                    if label == "" then
                        label = tostring(control.kind or "")
                    end
                    if #label > 96 then
                        label = label:sub(1, 93) .. "..."
                    end
                    parts[#parts + 1] = string.format(
                        "%s text=%s pos=(%.1f,%.1f) d=%.1f",
                        label,
                        tostring(control.text or ""),
                        tonumber(control.x) or 0,
                        tonumber(control.y) or 0,
                        tonumber(control.distance) or 0
                    )
                end
                nearby_detail = " nearby=" .. table.concat(parts, " | ")
            end
        end
        state.next_task_button_click_at = current_time + TASK_BUTTON_RETRY_INTERVAL_MS
        trace_main_task_call("fetch", "failed", tostring(selected_err or fetch_err or ui_snapshot_err or "main task target unavailable."))
        log_throttled(ctx, "task_button_fetch_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] main task button fetch failed: "
                .. tostring(selected_err or fetch_err or ui_snapshot_err or "main task target unavailable.")
                .. nearby_detail)
        return false, selected_err or fetch_err or ui_snapshot_err or "main task target unavailable."
    end

    local click_ok, resolved_target_or_err = click_anchor_button_target(target, target_source)
    if not click_ok then
        state.next_task_button_click_at = current_time + TASK_BUTTON_RETRY_INTERVAL_MS
        trace_main_task_call(tostring(target_source), "click_failed", tostring(resolved_target_or_err))
        log_throttled(ctx, "task_button_click_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] main task button click failed: " .. tostring(resolved_target_or_err))
        return false, resolved_target_or_err
    end

    target = resolved_target_or_err
    apply_main_task_click_result(target, hint_x, hint_y)
    trace_main_task_call(tostring(target_source), "success", string.format(
        "addr=%s pos=(%s,%s)",
        tostring(target.addr or ""),
        tostring(target.x or ""),
        tostring(target.y or "")
    ))
    logger(ctx).info(string.format(
        "[Leveling] main task button clicked | task=%s mode=%s source=%s label=%s kind=%s addr=%s pos=(%s,%s) hint=(%.2f,%.2f) distance=%.2f",
        tostring(M.current_task_log_name() or ""),
        preserve_target == true and "soft_refresh" or "hard_refresh",
        tostring(target_source),
        tostring(MAIN_TASK_BUTTON_STEP.label),
        tostring(target.kind or ""),
        tostring(target.addr or ""),
        tostring(target.x or ""),
        tostring(target.y or ""),
        tonumber(hint_x) or 0,
        tonumber(hint_y) or 0,
        tonumber(target.distance) or 0
    ))
    return true
end

function M.click_main_task_button(ctx, current_time, opts)
    return click_main_task_button(ctx, current_time, opts)
end

function M.build_treasure_hooks(ctx)
    return {
        current_task_name = function()
            return M.current_task_log_name() or state.current_task_name or ""
        end,
        current_task_detail = function()
            return M.current_task_log_detail() or state.current_task_detail or ""
        end,
        current_map_name = function()
            return state.current_map_name or ""
        end,
        fetch_locator_button_target = function(inner_ctx, step)
            return fetch_locator_button_target(inner_ctx, step)
        end,
        click_locator_button_target = function(inner_ctx, step, target)
            return click_locator_button_target(inner_ctx, step, target)
        end,
        click_task_panel_entry = function(inner_ctx, query)
            local nav_mod = nav_api(inner_ctx)
            if type(nav_mod) ~= "table" or type(nav_mod.click_task_panel_entry) ~= "function" then
                return false, "nav.click_task_panel_entry is unavailable."
            end
            return nav_mod.click_task_panel_entry(query, nil, {
                exact = false
            })
        end,
        get_main_task_path = function(inner_ctx)
            local nav_mod = nav_api(inner_ctx)
            if type(nav_mod) ~= "table" or type(nav_mod.get_main_task_path) ~= "function" then
                return nil, "nav.get_main_task_path is unavailable."
            end
            return nav_mod.get_main_task_path()
        end,
        sync_task_path_target = function(inner_ctx, player_x, player_y, current_time)
            return sync_task_path_target(inner_ctx, player_x, player_y, current_time)
        end,
        assign_task_target = function(inner_ctx, current_time, target)
            return assign_task_target(inner_ctx, current_time, target)
        end,
        clear_task_target_state = function()
            return clear_task_target_state()
        end,
        clear_task_combat_state = function()
            return clear_task_combat_state()
        end,
        hold_navigation = function(inner_ctx, current_time, reason)
            return hold_navigation(inner_ctx, current_time, reason)
        end,
        issue_combat_pulse = function(inner_ctx, current_time, reason, ignore_move_guard)
            return issue_combat_pulse(inner_ctx, current_time, reason, ignore_move_guard)
        end,
        schedule_task_refresh_after_transition = function(inner_ctx, current_time, reason, wait_ms, opts)
            return schedule_task_refresh_after_transition(inner_ctx, current_time, reason, wait_ms, opts)
        end,
        issue_move = function(inner_ctx, current_time, target)
            return issue_move(inner_ctx, current_time, target)
        end,
        press_interact = function(inner_ctx)
            return press_keyboard_hotkey(inner_ctx, now_ms(inner_ctx), VK_D, "treasure interact")
        end,
        press_loot_key = function(inner_ctx)
            return press_keyboard_hotkey(inner_ctx, now_ms(inner_ctx), M.VK_A, "treasure pickup loot")
        end,
        try_click_main_task_button = function(inner_ctx, current_time)
            return click_main_task_button(inner_ctx, current_time)
        end,
        find_task_monsters = function(inner_ctx, current_time, player_x, player_y)
            return M.find_task_monsters(inner_ctx, current_time, player_x, player_y)
        end,
        enum_portals = function(inner_ctx)
            local nav_mod = nav_api(inner_ctx)
            if type(nav_mod) ~= "table" or type(nav_mod.enum_portals) ~= "function" then
                return nil, "nav.enum_portals is unavailable."
            end
            return nav_mod.enum_portals()
        end,
        extract_position = function(inner_ctx, item)
            local nav_mod = nav_api(inner_ctx)
            if type(nav_mod) == "table" and type(nav_mod.extract_position) == "function" then
                local x, y, z = nav_mod.extract_position(item)
                if x ~= nil and y ~= nil then
                    return x, y, z
                end
            end
            return extract_position_from_item(inner_ctx, item)
        end,
        enum_ground_items = function(inner_ctx)
            local nav_mod = nav_api(inner_ctx)
            if type(nav_mod) ~= "table" or type(nav_mod.enum_ground_items) ~= "function" then
                return nil, "nav.enum_ground_items is unavailable."
            end
            return nav_mod.enum_ground_items()
        end,
        enum_ui = function(inner_ctx)
            local nav_mod = nav_api(inner_ctx)
            if type(nav_mod) ~= "table" or type(nav_mod.enum_ui) ~= "function" then
                return nil, "nav.enum_ui is unavailable."
            end
            return nav_mod.enum_ui()
        end,
        log_info = function(inner_ctx, message)
            logger(inner_ctx).info(message)
        end,
        log_throttled = function(inner_ctx, key, level, interval_ms, message)
            log_throttled(inner_ctx, key, level, interval_ms, message)
        end
    }
end

function M.maybe_handle_treasure_dungeon(ctx, current_time, player_x, player_y, player_z)
    if type(M._leveling_treasure) ~= "table"
        or type(M._leveling_treasure.maybe_handle) ~= "function"
        or type(M.TREASURE_DUNGEON_CONFIGS) ~= "table"
        or #M.TREASURE_DUNGEON_CONFIGS == 0
    then
        return false
    end
    return M._leveling_treasure.maybe_handle(
        ctx,
        state,
        M.TREASURE_DUNGEON_CONFIGS,
        M.build_treasure_hooks(ctx),
        current_time,
        player_x,
        player_y,
        player_z
    )
end

function M.maybe_override_treasure_task_target(ctx, current_time, player_x, player_y)
    if type(M._leveling_treasure) ~= "table"
        or type(M._leveling_treasure.provide_task_target_override) ~= "function"
        or type(M.TREASURE_DUNGEON_CONFIGS) ~= "table"
        or #M.TREASURE_DUNGEON_CONFIGS == 0
    then
        return false
    end
    return M._leveling_treasure.provide_task_target_override(
        ctx,
        state,
        M.TREASURE_DUNGEON_CONFIGS,
        M.build_treasure_hooks(ctx),
        current_time,
        player_x,
        player_y
    )
end

local function maybe_refresh_task_button_during_follow(ctx, current_time, target_distance, force_refresh)
    if type(target_distance) ~= "number" then
        return false
    end
    if target_distance <= TARGET_REACHED_DISTANCE then
        return false
    end
    if state.require_task_button_refresh == true then
        return false
    end
    if force_refresh ~= true then
        return false
    end

    local clicked, click_err = click_main_task_button(ctx, current_time)
    if clicked then
        log_throttled(ctx, "task_button_keepalive", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] follow refresh main task button | reason=stalled distance=%.2f stall_retry_count=%d",
            tonumber(target_distance) or 0,
            tonumber(state.stall_retry_count) or 0
        ))
        return true
    end

    state.next_follow_task_button_refresh_at = current_time + TASK_BUTTON_RETRY_INTERVAL_MS
    log_throttled(ctx, "task_button_keepalive_failed", "warn", LOG_THROTTLE_MS,
        "[Leveling] follow refresh main task button failed: " .. tostring(click_err))
    return false, click_err
end

local function maybe_refresh_task_button_for_route_deviation(ctx, current_time, player_x, player_y, target)
    if state.task_combat_force_kite == true or tostring(state.stage or "") == "task_combat_kite" then
        return false
    end
    if type(target) ~= "table" or tostring(target.source or "") ~= "task_path" then
        return false
    end
    local retry_cfg, retry_task_name = M.current_task_retry_call_config()
    local refresh_cooldown_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.interval_ms)
        or TASK_PATH_DEVIATION_REFRESH_COOLDOWN_MS
    if current_time < (tonumber(state.next_task_path_deviation_refresh_at) or 0) then
        return false
    end
    if (tonumber(state.task_path_wait_until) or 0) > current_time then
        return false
    end
    if state.require_task_button_refresh == true then
        return false
    end

    local route = state.task_path_route
    local path = state.task_path
    if type(route) ~= "table" or type(path) ~= "table" or #path == 0 then
        return false
    end

    local current_distance = tonumber(target.current_distance)
        or distance_2d(player_x, player_y, target.x, target.y)
    local best_distance = tonumber(route.point_best_distance)
    if type(current_distance) ~= "number"
        or type(best_distance) ~= "number"
        or best_distance == math.huge
    then
        return false
    end

    local nearest_index = tonumber(route.nearest_index)
    local nearest_distance = tonumber(route.nearest_distance)
    if nearest_index == nil or nearest_distance == nil then
        nearest_index, nearest_distance = find_nearest_path_index(player_x, player_y, path)
    end
    if nearest_index == nil or nearest_distance == nil then
        return false
    end

    local route_index = tonumber(route.index) or 0
    local index_delta = math.abs(nearest_index - route_index)
    local distance_regressed = current_distance - best_distance
    local last_progress_at = tonumber(state.last_progress_at) or 0
    local point_started_at = tonumber(route.point_started_at) or 0
    local no_progress_ms = last_progress_at > 0 and (current_time - last_progress_at) or 0
    local point_stagnant_ms = point_started_at > 0 and (current_time - point_started_at) or 0
    local deviation_stagnant_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.require_no_progress_ms)
        or 2200
    local point_stagnant_required_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.require_point_stagnant_ms)
        or deviation_stagnant_ms
    local off_segment_distance = tonumber(type(retry_cfg) == "table" and retry_cfg.off_segment_distance)
        or math.max(
        TASK_PATH_POINT_ARRIVE_TOLERANCE * 2,
        TASK_INTERACTION_APPROACH_DISTANCE * 2
    )
    local moved_far_off_current_segment = nearest_distance >= off_segment_distance
    local deviation_distance = tonumber(type(retry_cfg) == "table" and retry_cfg.deviation_distance)
        or TASK_PATH_DEVIATION_REFRESH_DISTANCE
    local should_refresh = distance_regressed >= TASK_PATH_DEVIATION_REFRESH_DISTANCE
        and no_progress_ms >= deviation_stagnant_ms
        and point_stagnant_ms >= point_stagnant_required_ms
        and (
            moved_far_off_current_segment
            or current_distance >= TASK_PATH_REANCHOR_DISTANCE
            or index_delta >= TASK_PATH_REANCHOR_INDEX_DELTA
        )

    if deviation_distance ~= TASK_PATH_DEVIATION_REFRESH_DISTANCE then
        should_refresh = distance_regressed >= deviation_distance
            and no_progress_ms >= deviation_stagnant_ms
            and point_stagnant_ms >= point_stagnant_required_ms
            and (
                moved_far_off_current_segment
                or current_distance >= TASK_PATH_REANCHOR_DISTANCE
                or index_delta >= TASK_PATH_REANCHOR_INDEX_DELTA
            )
    end

    if not should_refresh then
        return false
    end

    state.next_task_path_deviation_refresh_at = current_time + refresh_cooldown_ms
    local clicked, click_err = click_main_task_button(ctx, current_time)
    if clicked then
        if type(retry_cfg) == "table" then
            logger(ctx).info(string.format(
                "[Leveling] task retry call triggered | task=%s reason=route_deviation current_distance=%.2f best_distance=%.2f nearest_distance=%.2f no_progress_ms=%d point_stagnant_ms=%d route_index=%d nearest_index=%d cooldown_ms=%d deviation_distance=%.2f",
                tostring(retry_task_name or ""),
                tonumber(current_distance) or 0,
                tonumber(best_distance) or 0,
                tonumber(nearest_distance) or 0,
                tonumber(no_progress_ms) or 0,
                tonumber(point_stagnant_ms) or 0,
                route_index,
                nearest_index,
                tonumber(refresh_cooldown_ms) or 0,
                tonumber(deviation_distance) or 0
            ))
        else
            logger(ctx).info(string.format(
                "[Leveling] route deviation detected, refresh main task button | current_distance=%.2f best_distance=%.2f nearest_distance=%.2f no_progress_ms=%d point_stagnant_ms=%d route_index=%d nearest_index=%d",
                tonumber(current_distance) or 0,
                tonumber(best_distance) or 0,
                tonumber(nearest_distance) or 0,
                tonumber(no_progress_ms) or 0,
                tonumber(point_stagnant_ms) or 0,
                route_index,
                nearest_index
            ))
        end
        return true
    end

    log_throttled(ctx, "task_button_route_deviation_failed", "warn", LOG_THROTTLE_MS,
        "[Leveling] route deviation task refresh failed: " .. tostring(click_err))
    return false, click_err
end

local function maybe_refresh_task_button_for_path_loss(ctx, current_time, target, destination_distance)
    if state.task_combat_force_kite == true or tostring(state.stage or "") == "task_combat_kite" then
        return false
    end
    if type(target) ~= "table" or tostring(target.source or "") ~= "task_path" then
        return false
    end
    local retry_cfg, retry_task_name = M.current_task_retry_call_config()
    local refresh_cooldown_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.interval_ms)
        or TASK_PATH_DEVIATION_REFRESH_COOLDOWN_MS
    if current_time < (tonumber(state.next_task_path_deviation_refresh_at) or 0) then
        return false
    end
    if (tonumber(state.task_path_wait_until) or 0) > current_time then
        return false
    end
    if state.require_task_button_refresh == true then
        return false
    end

    local route = state.task_path_route
    if type(route) ~= "table" then
        return false
    end

    local current_distance = tonumber(target.current_distance)
    if type(current_distance) ~= "number" then
        return false
    end

    local last_move_call_at = tonumber(state.last_move_call_at) or 0
    local last_progress_at = tonumber(state.last_progress_at) or 0
    local point_started_at = tonumber(route.point_started_at) or 0
    if last_move_call_at <= 0 or last_progress_at <= 0 or point_started_at <= 0 then
        return false
    end

    local no_progress_ms = current_time - last_progress_at
    local point_stagnant_ms = current_time - point_started_at
    local point_arrived = current_distance <= TASK_PATH_POINT_ARRIVE_TOLERANCE
    local next_index = step_path_index(state.task_path, route.index, route.direction)
    local route_endpoint_blocked = point_arrived
        and next_index == nil
        and type(destination_distance) == "number"
        and destination_distance > TARGET_REACHED_DISTANCE
    local progress_refresh_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.require_no_progress_ms)
        or TASK_PATH_LOST_REFRESH_AFTER_MS
    local point_refresh_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.require_point_stagnant_ms)
        or progress_refresh_ms
    local route_endpoint_refresh_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.route_endpoint_refresh_ms)
        or 900
    local move_grace_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.move_grace_ms)
        or STUCK_MOVE_GRACE_MS
    local refresh_after_ms = route_endpoint_blocked and route_endpoint_refresh_ms or progress_refresh_ms
    local point_refresh_after_ms = route_endpoint_blocked and route_endpoint_refresh_ms or point_refresh_ms

    if no_progress_ms < refresh_after_ms
        or point_stagnant_ms < point_refresh_after_ms
        or current_time - last_move_call_at < move_grace_ms
    then
        return false
    end

    state.next_task_path_deviation_refresh_at = current_time + refresh_cooldown_ms
    local clicked, click_err = click_main_task_button(ctx, current_time)
    if clicked then
        if type(retry_cfg) == "table" then
            logger(ctx).info(string.format(
                "[Leveling] task retry call triggered | task=%s reason=%s current_distance=%.2f destination_distance=%s no_progress_ms=%d point_stagnant_ms=%d route_index=%d cooldown_ms=%d",
                tostring(retry_task_name or ""),
                route_endpoint_blocked and "path_endpoint" or "path_loss",
                tonumber(current_distance) or 0,
                type(destination_distance) == "number" and string.format("%.2f", destination_distance) or "nil",
                tonumber(no_progress_ms) or 0,
                tonumber(point_stagnant_ms) or 0,
                tonumber(route.index) or 0,
                tonumber(refresh_cooldown_ms) or 0
            ))
        elseif route_endpoint_blocked then
            logger(ctx).info(string.format(
                "[Leveling] task path endpoint exhausted before destination, refresh main task button | current_distance=%.2f destination_distance=%.2f no_progress_ms=%d point_stagnant_ms=%d route_index=%d",
                tonumber(current_distance) or 0,
                tonumber(destination_distance) or 0,
                tonumber(no_progress_ms) or 0,
                tonumber(point_stagnant_ms) or 0,
                tonumber(route.index) or 0
            ))
        else
            logger(ctx).info(string.format(
                "[Leveling] task path progress paused, refresh main task button | current_distance=%.2f no_progress_ms=%d point_stagnant_ms=%d route_index=%d",
                tonumber(current_distance) or 0,
                tonumber(no_progress_ms) or 0,
                tonumber(point_stagnant_ms) or 0,
                tonumber(route.index) or 0
            ))
        end
        return true
    end

    log_throttled(ctx, "task_button_path_loss_failed", "warn", LOG_THROTTLE_MS,
        "[Leveling] task path stalled refresh failed: " .. tostring(click_err))
    return false, click_err
end

function M.maybe_refresh_task_button_for_follow_idle(ctx, current_time, target, destination_distance, goal_distance)
    if state.task_combat_force_kite == true or tostring(state.stage or "") == "task_combat_kite" then
        return false
    end
    if type(target) ~= "table" or tostring(target.source or "") ~= "task_path" then
        return false
    end
    if type(goal_distance) ~= "number" or goal_distance <= TARGET_REACHED_DISTANCE then
        return false
    end
    if current_time < (tonumber(state.next_follow_idle_refresh_at) or 0) then
        return false
    end
    if (tonumber(state.task_path_wait_until) or 0) > current_time then
        return false
    end
    if state.require_task_button_refresh == true then
        return false
    end

    local route = state.task_path_route
    if type(route) ~= "table" then
        return false
    end

    local retry_cfg, retry_task_name = M.current_task_retry_call_config()
    local no_progress_ms = 0
    local point_stagnant_ms = 0
    local no_position_change_ms = 0
    local last_progress_at = tonumber(state.last_progress_at) or 0
    local point_started_at = tonumber(route.point_started_at) or 0
    local last_position_change_at = tonumber(state.last_position_change_at) or 0
    if last_progress_at > 0 then
        no_progress_ms = current_time - last_progress_at
    end
    if point_started_at > 0 then
        point_stagnant_ms = current_time - point_started_at
    end
    if last_position_change_at > 0 then
        no_position_change_ms = current_time - last_position_change_at
    end

    local current_distance = tonumber(target.current_distance)
    if type(current_distance) ~= "number" then
        return false
    end

    local base_no_progress_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.require_no_progress_ms)
        or STUCK_RETRY_INTERVAL_MS
    local base_point_stagnant_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.require_point_stagnant_ms)
        or base_no_progress_ms
    local idle_refresh_after_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.idle_refresh_after_ms)
        or math.max(5200, base_no_progress_ms + 2200)
    local idle_point_refresh_after_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.idle_point_stagnant_ms)
        or math.max(idle_refresh_after_ms, base_point_stagnant_ms + 1800)
    local idle_move_failures = tonumber(type(retry_cfg) == "table" and retry_cfg.idle_move_failures)
        or 2
    local refresh_cooldown_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.idle_refresh_cooldown_ms)
        or 3200
    local no_position_change_refresh_ms = tonumber(type(retry_cfg) == "table" and retry_cfg.no_position_change_refresh_ms)
        or 6000
    local last_move_failure_at = tonumber(state.last_move_failure_at) or 0
    local last_move_call_at = tonumber(state.last_move_call_at) or 0
    local move_failure_streak = tonumber(state.move_failure_streak) or 0
    local failure_window_ms = math.max(
        idle_refresh_after_ms,
        tonumber(type(retry_cfg) == "table" and retry_cfg.idle_move_failure_window_ms) or 2600
    )
    local recent_move_failure = last_move_failure_at > 0
        and current_time - last_move_failure_at <= failure_window_ms
    local stall_retry_count = tonumber(state.stall_retry_count) or 0
    local move_grace_ms = math.max(
        STUCK_MOVE_GRACE_MS,
        tonumber(type(retry_cfg) == "table" and retry_cfg.move_grace_ms) or STUCK_MOVE_GRACE_MS
    )
    local no_position_change_refresh = last_move_call_at > 0
        and current_time - last_move_call_at >= move_grace_ms
        and no_position_change_ms >= no_position_change_refresh_ms
    local strong_idle = no_progress_ms >= (idle_refresh_after_ms + 2200)
        and point_stagnant_ms >= math.max(idle_point_refresh_after_ms, idle_refresh_after_ms)
    local should_refresh = no_position_change_refresh
        or (
            no_progress_ms >= idle_refresh_after_ms
            and point_stagnant_ms >= idle_point_refresh_after_ms
            and (
                (move_failure_streak >= idle_move_failures and recent_move_failure)
                or stall_retry_count >= 2
                or strong_idle
            )
        )

    if not should_refresh then
        return false
    end

    state.next_follow_idle_refresh_at = current_time + refresh_cooldown_ms
    local clicked, click_err = click_main_task_button(ctx, current_time)
    if clicked then
        if no_position_change_refresh then
            logger(ctx).info(string.format(
                "[Leveling] task retry call triggered | task=%s reason=no_position_change current_distance=%.2f destination_distance=%s goal_distance=%.2f no_position_change_ms=%d no_progress_ms=%d point_stagnant_ms=%d move_grace_ms=%d cooldown_ms=%d",
                tostring(retry_task_name or state.current_task_name or ""),
                tonumber(current_distance) or 0,
                type(destination_distance) == "number" and string.format("%.2f", destination_distance) or "nil",
                tonumber(goal_distance) or 0,
                tonumber(no_position_change_ms) or 0,
                tonumber(no_progress_ms) or 0,
                tonumber(point_stagnant_ms) or 0,
                tonumber(move_grace_ms) or 0,
                tonumber(refresh_cooldown_ms) or 0
            ))
        elseif type(retry_cfg) == "table" then
            logger(ctx).info(string.format(
                "[Leveling] task retry call triggered | task=%s reason=follow_idle current_distance=%.2f destination_distance=%s goal_distance=%.2f no_progress_ms=%d point_stagnant_ms=%d move_failure_streak=%d stall_retry_count=%d cooldown_ms=%d",
                tostring(retry_task_name or ""),
                tonumber(current_distance) or 0,
                type(destination_distance) == "number" and string.format("%.2f", destination_distance) or "nil",
                tonumber(goal_distance) or 0,
                tonumber(no_progress_ms) or 0,
                tonumber(point_stagnant_ms) or 0,
                tonumber(move_failure_streak) or 0,
                tonumber(stall_retry_count) or 0,
                tonumber(refresh_cooldown_ms) or 0
            ))
        else
            logger(ctx).info(string.format(
                "[Leveling] follow path idle detected, refresh main task button | current_distance=%.2f destination_distance=%s goal_distance=%.2f no_progress_ms=%d point_stagnant_ms=%d move_failure_streak=%d stall_retry_count=%d",
                tonumber(current_distance) or 0,
                type(destination_distance) == "number" and string.format("%.2f", destination_distance) or "nil",
                tonumber(goal_distance) or 0,
                tonumber(no_progress_ms) or 0,
                tonumber(point_stagnant_ms) or 0,
                tonumber(move_failure_streak) or 0,
                tonumber(stall_retry_count) or 0
            ))
        end
        return true
    end

    log_throttled(
        ctx,
        no_position_change_refresh and "task_button_no_position_change_failed" or "task_button_follow_idle_failed",
        "warn",
        LOG_THROTTLE_MS,
        (no_position_change_refresh
            and "[Leveling] no-position-change task refresh failed: "
            or "[Leveling] follow idle task refresh failed: ")
            .. tostring(click_err)
    )
    return false, click_err
end

local function maybe_soft_refresh_task_button_during_follow(ctx, current_time, target_distance)
    return false
end

local function update_task_target(ctx, current_time, player_x, player_y)
    if M.maybe_override_treasure_task_target(ctx, current_time, player_x, player_y) then
        state.next_task_refresh_at = current_time + TASK_REFRESH_INTERVAL_MS
        return true
    end

    local nav_mod = nav_api(ctx)
    local refresh_interval_ms = TASK_REFRESH_INTERVAL_MS
    if (tonumber(state.task_path_wait_until) or 0) > current_time then
        refresh_interval_ms = TASK_PATH_FETCH_POLL_INTERVAL_MS
    end
    state.next_task_refresh_at = current_time + refresh_interval_ms

    local path, path_err = nil, nil
    if type(nav_mod) == "table" and type(nav_mod.get_main_task_path) == "function" then
        path, path_err = nav_mod.get_main_task_path()
    end
    if type(path) == "table" then
        local live_signature = build_path_signature(path)
        local snapshot_signature = build_path_signature(state.task_path)
        local adopt_snapshot = state.task_path_refresh_requested == true
            or type(state.task_path) ~= "table"
            or #state.task_path == 0
            or type(state.task_path_route) ~= "table"
        if adopt_snapshot then
            local snapshot, raw_count, compress_mode = clone_task_path(path)
            if type(snapshot) == "table" then
                state.task_path = snapshot
                state.task_path_count = #snapshot
                state.task_path_raw_count = tonumber(raw_count) or #snapshot
                state.task_path_compress_mode = tostring(compress_mode or "")
                state.task_path_route = nil
                state.task_path_refresh_requested = false
                state.startup_task_path_reacquire_until = 0
                state.force_task_path_reacquire_until = 0
                state.force_task_path_reacquire_reason = nil
                state.force_task_path_reacquire_extra_ms = 0
                logger(ctx).info(string.format(
                    "[Leveling] task path snapshot adopted | points=%d raw_points=%d mode=%s signature=%s",
                    tonumber(state.task_path_count) or 0,
                    tonumber(state.task_path_raw_count) or tonumber(state.task_path_count) or 0,
                    tostring(state.task_path_compress_mode or ""),
                    tostring(live_signature)
                ))
                if (tonumber(state.global_task_portal_guard_until) or 0) > 0 then
                    state.global_task_portal_guard_until = math.max(
                        tonumber(state.global_task_portal_guard_until) or 0,
                        current_time + 2500
                    )
                end
            end
        elseif live_signature == snapshot_signature then
            local snapshot, raw_count, compress_mode = clone_task_path(path)
            if type(snapshot) == "table" then
                state.task_path = snapshot
                state.task_path_count = #snapshot
                state.task_path_raw_count = tonumber(raw_count) or #snapshot
                state.task_path_compress_mode = tostring(compress_mode or "")
            end
        end
    end

    local pos, pos_err = nil, nil
    if type(nav_mod) == "table" and type(nav_mod.get_main_task_pos) == "function" then
        pos, pos_err = nav_mod.get_main_task_pos()
    end
    if type(pos) == "table" then
        state.task_pos = pos
        if not is_valid_world_point(pos) then
            log_throttled(ctx, "task_pos_api_returned_invalid_world_point", "warn", LOG_THROTTLE_MS, string.format(
                "[Leveling] GetMainTaskPos returned unusable world point | %s",
                M.format_world_point_for_log(pos)
            ))
        end
    end

    local target = nil
    if type(state.task_path) == "table" and #state.task_path > 0 then
        target = select(1, sync_task_path_target(ctx, player_x, player_y, current_time))
        state.last_task_path_sync_at = current_time
    end
    if not target and is_valid_world_point(state.task_pos) then
        local force_task_path_reacquire_active =
            (tonumber(state.force_task_path_reacquire_until) or 0) > current_time
        if (tonumber(state.task_pos_reject_until) or 0) > current_time then
            log_throttled(ctx, "task_pos_temporarily_rejected", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] task_pos temporarily rejected while waiting for task_path | reason=%s target=%.2f, %.2f, %.2f",
                tostring(state.task_pos_reject_reason or ""),
                tonumber(state.task_pos.x) or 0,
                tonumber(state.task_pos.y) or 0,
                tonumber(state.task_pos.z) or 0
            ))
            return false, "task_pos rejected while waiting for task_path"
        end
        if force_task_path_reacquire_active then
            log_throttled(ctx, "task_pos_rejected_force_reacquire", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] task_pos rejected during forced task_path reacquire | reason=%s remaining_ms=%d target=%.2f, %.2f, %.2f",
                tostring(state.force_task_path_reacquire_reason or ""),
                math.max(0, (tonumber(state.force_task_path_reacquire_until) or current_time) - current_time),
                tonumber(state.task_pos.x) or 0,
                tonumber(state.task_pos.y) or 0,
                tonumber(state.task_pos.z) or 0
            ))
            return false, "task_pos rejected during forced task_path reacquire"
        end
        target = {
            x = state.task_pos.x,
            y = state.task_pos.y,
            z = state.task_pos.z,
            source = "task_pos",
            path_index = 0,
            path_points = tonumber(state.task_path_count) or 0,
            move_interval_ms = M.TASK_POS_MOVE_INTERVAL_MS
        }
    end

    if target then
        state.task_path_wait_until = 0
        state.task_pos_reject_until = 0
        state.task_pos_reject_reason = nil
        state.force_task_path_reacquire_until = 0
        state.force_task_path_reacquire_reason = nil
        state.force_task_path_reacquire_extra_ms = 0
        assign_task_target(ctx, current_time, target)
        return true
    end

    if type(state.task_target) == "table"
        and current_time - (tonumber(state.task_target_updated_at) or 0) <= TASK_STALE_KEEP_MS
    then
        log_throttled(ctx, "task_stale_keep", "warn", LOG_THROTTLE_MS,
            "[Leveling] main task refresh failed, keep last target for a short time.")
        return true
    end

    clear_task_target_state()
    log_throttled(ctx, "task_missing", "warn", LOG_THROTTLE_MS, string.format(
        "[Leveling] main task data unavailable after task button click. path_err=%s pos_err=%s nav=%s last_call=%s",
        tostring(path_err),
        tostring(pos_err),
        M.nav_debug_state_text(ctx),
        M.format_last_main_task_call_debug(current_time)
    ))
    return false, path_err or pos_err or "main task data unavailable."
end

M.find_current_task_npc = function(ctx, current_time, player_x, player_y)
    current_time = tonumber(current_time) or now_ms(ctx)
    if current_time < (tonumber(state.next_npc_scan_at) or 0) then
        return state.cached_nearest_npc, state.cached_npc_error
    end

    state.next_npc_scan_at = current_time + NPC_SCAN_INTERVAL_MS

    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.enum_npcs) ~= "function" then
        state.cached_nearest_npc = nil
        state.cached_npc_error = "nav.enum_npcs is unavailable."
        return nil, state.cached_npc_error
    end

    local items, enum_err = nav_mod.enum_npcs()
    if type(items) ~= "table" then
        state.cached_nearest_npc = nil
        state.cached_npc_error = enum_err or "EnumNPC failed."
        return nil, state.cached_npc_error
    end

    local destination = build_task_destination_point(player_x, player_y)
    if not destination then
        state.cached_nearest_npc = nil
        state.cached_npc_error = "Current task destination is unavailable."
        return nil, state.cached_npc_error
    end

    local nearest = nil
    for _, item in ipairs(items) do
        local x, y, z = extract_position_from_item(ctx, item)
        if x ~= nil and y ~= nil then
            local player_distance = distance_2d(player_x, player_y, x, y)
            local task_distance = distance_2d(destination.x, destination.y, x, y)
            if player_distance <= NPC_INTERACT_DISTANCE and task_distance <= NPC_TASK_TARGET_MAX_DISTANCE then
                local score = player_distance + task_distance
                if not nearest or score < nearest.score then
                    nearest = {
                        item = item,
                        x = x,
                        y = y,
                        z = z,
                        distance = player_distance,
                        task_distance = task_distance,
                        label = npc_label(item),
                        destination_x = destination.x,
                        destination_y = destination.y,
                        destination_z = destination.z,
                        destination_source = destination.source,
                        score = score
                    }
                end
            end
        end
    end

    state.cached_nearest_npc = nearest
    state.cached_npc_error = nearest and nil or string.format(
        "No NPC matched current task target. destination=%.2f, %.2f source=%s",
        tonumber(destination.x) or 0,
        tonumber(destination.y) or 0,
        tostring(destination.source or "")
    )
    return nearest, state.cached_npc_error
end

local function arm_dialogue_followup(current_time, origin, label, refresh_on_timeout)
    state.last_dialogue_at = current_time
    state.dialogue_escape_due_at = 0
    state.dialogue_confirm_deadline_at = 0
    state.next_dialogue_probe_at = 0
    state.next_dialogue_jump_scan_at = 0
    state.next_dialogue_jump_click_at = 0
    state.dialogue_ui_confirmed = false
    state.dialogue_ui_match = nil
    state.task_update_wait_until = math.max(
        tonumber(state.task_update_wait_until) or 0,
        current_time + POST_DIALOGUE_SETTLE_MS
    )
    state.require_task_button_refresh = false
    state.pending_interaction_origin = tostring(origin or "")
    state.pending_interaction_label = tostring(label or "")
    state.pending_interaction_refresh_on_timeout = refresh_on_timeout == true
    M.clear_task_dialogue_flow_state()
    M.clear_post_dialogue_flow_state()
    state.pause_combat_until = current_time + POST_DIALOGUE_SETTLE_MS
    state.next_move_at = math.max(tonumber(state.next_move_at) or 0, current_time + POST_DIALOGUE_SETTLE_MS)
    state.cached_nearest_npc = nil
    state.cached_npc_error = nil
    state.cached_task_monsters = nil
    state.cached_task_monster_error = nil
    state.cached_interaction_prompt_target = nil
    state.cached_interaction_prompt_error = nil
    state.next_interaction_prompt_scan_at = 0
end

function M.arm_npc_dialogue_combat_retry(current_time, npc, opts)
    opts = type(opts) == "table" and opts or {}
    local retry_timeout_ms = math.max(6000, tonumber(opts.combat_retry_timeout_ms) or 20000)
    local source = tostring(opts.combat_retry_source or "current_task_npc")
    local npc_x = tonumber(opts.point_x) or tonumber(npc and npc.x)
    local npc_y = tonumber(opts.point_y) or tonumber(npc and npc.y)
    local npc_z = tonumber(opts.point_z) or tonumber(npc and npc.z)

    state.npc_dialogue_combat_retry_active = true
    state.npc_dialogue_combat_retry_source = source
    state.npc_dialogue_combat_retry_task_name = tostring(M.current_task_log_name() or state.current_task_name or "")
    state.npc_dialogue_combat_retry_task_detail = tostring(M.current_task_log_detail() or state.current_task_detail or "")
    state.npc_dialogue_combat_retry_npc_label = tostring(opts.npc_label or npc and npc.label or "")
    state.npc_dialogue_combat_retry_route_action_key = tostring(opts.combat_retry_route_action_key or "")
    state.npc_dialogue_combat_retry_point_x = npc_x
    state.npc_dialogue_combat_retry_point_y = npc_y
    state.npc_dialogue_combat_retry_point_z = npc_z
    state.npc_dialogue_combat_retry_search_radius = tonumber(opts.search_radius)
    state.npc_dialogue_combat_retry_interact_radius = tonumber(opts.interact_radius)
    state.npc_dialogue_combat_retry_move_interval_ms = tonumber(opts.move_interval_ms)
    state.npc_dialogue_combat_retry_deadline_at = (tonumber(current_time) or 0) + retry_timeout_ms
    state.npc_dialogue_combat_retry_next_retry_at = tonumber(current_time) or 0
    state.npc_dialogue_combat_retry_combat_seen = false

    logger(state.log_ctx).info(string.format(
        "[Leveling] npc dialogue combat retry armed | task=%s detail=%s source=%s npc=%s point=%.2f, %.2f, %.2f timeout_ms=%d",
        tostring(state.npc_dialogue_combat_retry_task_name or ""),
        tostring(state.npc_dialogue_combat_retry_task_detail or ""),
        source,
        tostring(state.npc_dialogue_combat_retry_npc_label or ""),
        tonumber(npc_x) or 0,
        tonumber(npc_y) or 0,
        tonumber(npc_z) or 0,
        retry_timeout_ms
    ))
end

function M.npc_dialogue_combat_retry_task_matches_current()
    local retry_task_name = normalize_map_name(state.npc_dialogue_combat_retry_task_name)
    local retry_task_detail = normalize_map_name(state.npc_dialogue_combat_retry_task_detail)
    local current_task_name = normalize_map_name(M.current_task_log_name() or state.current_task_name)
    local current_task_detail = normalize_map_name(M.current_task_log_detail() or state.current_task_detail)

    if retry_task_name ~= nil and current_task_name ~= nil and retry_task_name ~= current_task_name then
        return false, "task_changed"
    end
    if retry_task_detail ~= nil and current_task_detail ~= nil and retry_task_detail ~= current_task_detail then
        return false, "detail_changed"
    end
    return true, nil
end

function M.maybe_handle_npc_dialogue_combat_retry(ctx, current_time, player_x, player_y, player_z)
    if state.npc_dialogue_combat_retry_active ~= true
        or state.npc_dialogue_combat_retry_combat_seen ~= true
    then
        return false
    end

    local deadline_at = tonumber(state.npc_dialogue_combat_retry_deadline_at) or 0
    if deadline_at > 0 and current_time >= deadline_at then
        logger(ctx).warn(string.format(
            "[Leveling] npc dialogue combat retry expired | task=%s detail=%s source=%s npc=%s",
            tostring(state.npc_dialogue_combat_retry_task_name or ""),
            tostring(state.npc_dialogue_combat_retry_task_detail or ""),
            tostring(state.npc_dialogue_combat_retry_source or ""),
            tostring(state.npc_dialogue_combat_retry_npc_label or "")
        ))
        M.clear_npc_dialogue_combat_retry_state()
        return false
    end

    local task_ok, task_reason = M.npc_dialogue_combat_retry_task_matches_current()
    if task_ok ~= true then
        logger(ctx).info(string.format(
            "[Leveling] npc dialogue combat retry cleared after task change | reason=%s source=%s npc=%s old_task=%s old_detail=%s new_task=%s new_detail=%s",
            tostring(task_reason or ""),
            tostring(state.npc_dialogue_combat_retry_source or ""),
            tostring(state.npc_dialogue_combat_retry_npc_label or ""),
            tostring(state.npc_dialogue_combat_retry_task_name or ""),
            tostring(state.npc_dialogue_combat_retry_task_detail or ""),
            tostring(M.current_task_log_name() or state.current_task_name or ""),
            tostring(M.current_task_log_detail() or state.current_task_detail or "")
        ))
        M.clear_npc_dialogue_combat_retry_state()
        return false
    end

    if current_time < (tonumber(state.npc_dialogue_combat_retry_next_retry_at) or 0) then
        state.stage = "npc_dialogue_combat_retry_wait"
        hold_navigation(ctx, current_time, "npc_dialogue_combat_retry_wait")
        log_throttled(ctx, "npc_dialogue_combat_retry_wait", "info", 1000, string.format(
            "[Leveling] npc dialogue combat retry waiting | source=%s npc=%s retry_in=%dms",
            tostring(state.npc_dialogue_combat_retry_source or ""),
            tostring(state.npc_dialogue_combat_retry_npc_label or ""),
            math.max(0, (tonumber(state.npc_dialogue_combat_retry_next_retry_at) or current_time) - current_time)
        ))
        return true
    end

    local retry_source = tostring(state.npc_dialogue_combat_retry_source or "")
    if retry_source == "route_point_action_npc_dialogue" then
        local action_key = tostring(state.npc_dialogue_combat_retry_route_action_key or "")
        local action = M.find_route_point_action_by_key(action_key)
        if type(action) ~= "table" then
            logger(ctx).warn(string.format(
                "[Leveling] npc dialogue combat retry route action missing | action=%s npc=%s",
                action_key,
                tostring(state.npc_dialogue_combat_retry_npc_label or "")
            ))
            M.clear_npc_dialogue_combat_retry_state()
            return false
        end

        local state_key = M.route_point_action_state_key(action)
        local armed = M.arm_route_point_action_npc_dialogue(ctx, action, state_key, current_time, "combat_retry")
        if armed ~= true then
            logger(ctx).warn(string.format(
                "[Leveling] npc dialogue combat retry route action arm failed | action=%s npc=%s",
                action_key,
                tostring(state.npc_dialogue_combat_retry_npc_label or "")
            ))
            M.clear_npc_dialogue_combat_retry_state()
            return false
        end

        state.npc_dialogue_combat_retry_combat_seen = false
        state.npc_dialogue_combat_retry_next_retry_at = current_time + math.max(
            800,
            tonumber(type(action.dialogue) == "table" and action.dialogue.interact_retry_ms) or 1800
        )
        logger(ctx).info(string.format(
            "[Leveling] npc dialogue combat retry resumed via route action | action=%s npc=%s",
            action_key,
            tostring(state.npc_dialogue_combat_retry_npc_label or "")
        ))
        return M.maybe_handle_route_point_action_npc_dialogue(ctx, current_time, player_x, player_y, player_z)
    end

    logger(ctx).info(string.format(
        "[Leveling] npc dialogue combat retry released to normal npc flow | task=%s detail=%s npc=%s",
        tostring(state.npc_dialogue_combat_retry_task_name or ""),
        tostring(state.npc_dialogue_combat_retry_task_detail or ""),
        tostring(state.npc_dialogue_combat_retry_npc_label or "")
    ))
    M.clear_npc_dialogue_combat_retry_state()
    return false
end

M.find_task_monsters = function(ctx, current_time, player_x, player_y)
    current_time = tonumber(current_time) or now_ms(ctx)
    if current_time < (tonumber(state.next_monster_scan_at) or 0) then
        return state.cached_task_monsters, state.cached_task_monster_error
    end

    state.next_monster_scan_at = current_time + MONSTER_SCAN_INTERVAL_MS

    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.enum_monsters) ~= "function" then
        state.cached_task_monsters = nil
        state.cached_task_monster_error = "nav.enum_monsters is unavailable."
        return nil, state.cached_task_monster_error
    end

    local items, enum_err = nav_mod.enum_monsters()
    if type(items) ~= "table" then
        state.cached_task_monsters = nil
        state.cached_task_monster_error = enum_err or "EnumMonster failed."
        return nil, state.cached_task_monster_error
    end

    local destination = build_task_destination_point(player_x, player_y)
    local summary = {
        count = 0,
        total_count = 0,
        nearest = nil,
        nearest_any = nil,
        nearest_special = nil,
        special_count = 0,
        destination_source = destination and destination.source or nil
    }

    for _, item in ipairs(items) do
        local x, y, z = extract_position_from_item(ctx, item)
        if x ~= nil and y ~= nil then
            local label = npc_label(item)
            local special_kind = M.monster_special_kind(item, label)
            summary.total_count = summary.total_count + 1
            local player_distance = distance_2d(player_x, player_y, x, y)
            local task_distance = destination and distance_2d(destination.x, destination.y, x, y) or nil
            if not summary.nearest_any or player_distance < (tonumber(summary.nearest_any.distance) or math.huge) then
                summary.nearest_any = {
                    item = item,
                    x = x,
                    y = y,
                    z = z,
                    distance = player_distance,
                    task_distance = task_distance,
                    label = label,
                    special_kind = special_kind
                }
            end
            local near_player = player_distance <= TASK_MONSTER_PLAYER_DISTANCE
            if near_player then
                summary.count = summary.count + 1
                local score = player_distance + (task_distance or player_distance)
                if not summary.nearest or score < summary.nearest.score then
                    summary.nearest = {
                        item = item,
                        x = x,
                        y = y,
                        z = z,
                        distance = player_distance,
                        task_distance = task_distance,
                        label = label,
                        special_kind = special_kind,
                        score = score
                    }
                end
                if special_kind ~= nil then
                    summary.special_count = tonumber(summary.special_count) or 0
                    summary.special_count = summary.special_count + 1
                    if not summary.nearest_special or player_distance < (tonumber(summary.nearest_special.distance) or math.huge) then
                        summary.nearest_special = {
                            item = item,
                            x = x,
                            y = y,
                            z = z,
                            distance = player_distance,
                            task_distance = task_distance,
                            label = label,
                            special_kind = special_kind
                        }
                    end
                end
            end
        end
    end

    if summary.count > 0 then
        state.cached_task_monsters = summary
        state.cached_task_monster_error = nil
        return summary, nil
    end

    state.cached_task_monsters = nil
    local nearest_any = summary.nearest_any or {}
    state.cached_task_monster_error = destination
        and string.format(
            "No nearby monster around player. total=%d nearest=%s nearest_player_distance=%s destination=%.2f, %.2f source=%s",
            tonumber(summary.total_count) or 0,
            tostring(nearest_any.label or ""),
            nearest_any.distance ~= nil and string.format("%.2f", tonumber(nearest_any.distance) or 0) or "nil",
            tonumber(destination.x) or 0,
            tonumber(destination.y) or 0,
            tostring(destination.source or "")
        )
        or string.format(
            "No nearby monster around player and destination unavailable. total=%d nearest=%s nearest_player_distance=%s",
            tonumber(summary.total_count) or 0,
            tostring(nearest_any.label or ""),
            nearest_any.distance ~= nil and string.format("%.2f", tonumber(nearest_any.distance) or 0) or "nil"
        )
    return nil, state.cached_task_monster_error
end

function M.find_forced_kite_monster(task_monsters)
    if type(task_monsters) ~= "table" then
        return nil
    end

    for _, candidate in ipairs({
        task_monsters.nearest_special,
        task_monsters.nearest
    }) do
        if type(candidate) == "table" then
            local special_kind = tostring(candidate.special_kind or "")
            local label = trim(candidate.label or "")
            local matched_name = select(2, is_force_kite_monster_name(label))
            if special_kind == "forced_kite" or matched_name ~= nil then
                candidate.special_kind = "forced_kite"
                candidate.force_kite_name = matched_name or label
                return candidate
            end
        end
    end

    return nil
end

function M.mark_task_combat_seen(current_time, player_x, player_y, destination, task_monsters)
    current_time = tonumber(current_time) or 0
    if state.npc_dialogue_combat_retry_active == true
        and state.npc_dialogue_combat_retry_combat_seen ~= true
        and tostring(state.pending_interaction_origin or "") == "npc"
    then
        state.npc_dialogue_combat_retry_combat_seen = true
        state.npc_dialogue_combat_retry_next_retry_at = math.max(
            tonumber(state.npc_dialogue_combat_retry_next_retry_at) or 0,
            current_time + TASK_COMBAT_CLEAR_SETTLE_MS
        )
        state.dialogue_escape_due_at = 0
        state.dialogue_confirm_deadline_at = 0
        state.next_dialogue_probe_at = 0
        state.next_dialogue_jump_scan_at = 0
        state.next_dialogue_jump_click_at = 0
        state.dialogue_ui_confirmed = false
        state.dialogue_ui_match = nil
        state.task_update_wait_until = 0
        state.require_task_button_refresh = false
        clear_pending_interaction(true)
        logger(state.log_ctx).info(string.format(
            "[Leveling] npc dialogue interrupted by combat | task=%s detail=%s source=%s npc=%s monsters=%d retry_after_clear=true",
            tostring(state.npc_dialogue_combat_retry_task_name or M.current_task_log_name() or state.current_task_name or ""),
            tostring(state.npc_dialogue_combat_retry_task_detail or M.current_task_log_detail() or state.current_task_detail or ""),
            tostring(state.npc_dialogue_combat_retry_source or ""),
            tostring(state.npc_dialogue_combat_retry_npc_label or ""),
            tonumber(task_monsters and task_monsters.count) or 0
        ))
    end
    if (tonumber(state.task_combat_started_at) or 0) <= 0 then
        state.task_combat_started_at = current_time
    end
    state.task_combat_last_seen_at = current_time
    state.task_combat_last_count = tonumber(task_monsters and task_monsters.count) or 0
    local special = type(task_monsters) == "table" and task_monsters.nearest_special or nil
    if type(special) == "table"
        and tonumber(special.x) ~= nil
        and tonumber(special.y) ~= nil
    then
        state.task_combat_anchor_x = tonumber(special.x)
        state.task_combat_anchor_y = tonumber(special.y)
        state.task_combat_anchor_z = tonumber(special.z)
    elseif type(destination) == "table" and is_valid_world_point(destination) then
        state.task_combat_anchor_x = tonumber(destination.x)
        state.task_combat_anchor_y = tonumber(destination.y)
        state.task_combat_anchor_z = tonumber(destination.z)
    elseif player_x ~= nil and player_y ~= nil then
        state.task_combat_anchor_x = tonumber(player_x)
        state.task_combat_anchor_y = tonumber(player_y)
    end
end

function M.should_use_task_combat_kiting(current_time, task_monsters, hp_ratio)
    local special = task_monsters and task_monsters.nearest_special or nil
    if type(special) ~= "table" then
        return false
    end

    local special_distance = tonumber(special.distance)
    if special_distance ~= nil and special_distance > TASK_MONSTER_PLAYER_DISTANCE then
        return false
    end

    return true
end

local function build_task_combat_kite_points(anchor_x, anchor_y, anchor_z, radius)
    radius = tonumber(radius) or TASK_COMBAT_KITE_RADIUS
    return {
        {
            x = anchor_x + radius,
            y = anchor_y,
            z = anchor_z,
            source = "task_combat_kite",
            path_index = 1,
            path_points = 4
        },
        {
            x = anchor_x,
            y = anchor_y + radius,
            z = anchor_z,
            source = "task_combat_kite",
            path_index = 2,
            path_points = 4
        },
        {
            x = anchor_x - radius,
            y = anchor_y,
            z = anchor_z,
            source = "task_combat_kite",
            path_index = 3,
            path_points = 4
        },
        {
            x = anchor_x,
            y = anchor_y - radius,
            z = anchor_z,
            source = "task_combat_kite",
            path_index = 4,
            path_points = 4
        }
    }
end

function M.build_task_combat_kite_target(current_time, player_x, player_y, destination)
    local route_anchor_x = tonumber(state.task_combat_kite_anchor_route_x)
    local route_anchor_y = tonumber(state.task_combat_kite_anchor_route_y)
    local route_anchor_z = tonumber(state.task_combat_kite_anchor_route_z)
    local anchor_x = route_anchor_x or tonumber(state.task_combat_anchor_x) or tonumber(destination and destination.x) or tonumber(player_x)
    local anchor_y = route_anchor_y or tonumber(state.task_combat_anchor_y) or tonumber(destination and destination.y) or tonumber(player_y)
    local anchor_z = route_anchor_z or tonumber(state.task_combat_anchor_z) or tonumber(destination and destination.z)
    if anchor_x == nil or anchor_y == nil or player_x == nil or player_y == nil then
        return nil
    end

    state.task_combat_anchor_x = anchor_x
    state.task_combat_anchor_y = anchor_y
    state.task_combat_anchor_z = anchor_z

    local radius = tonumber(state.task_combat_kite_radius) or TASK_COMBAT_KITE_RADIUS
    local configured_template = type(state.task_combat_kite_template_points) == "table" and state.task_combat_kite_template_points or nil
    local configured_count = type(configured_template) == "table" and #configured_template or 0
    local configured_mode = configured_count >= 3
    local configured_switch_ms = math.max(
        TASK_COMBAT_KITE_SWITCH_MS,
        tonumber(state.task_combat_kite_switch_ms) or 2800
    )
    local generated_switch_ms = math.max(
        TASK_COMBAT_KITE_SWITCH_MS,
        tonumber(state.task_combat_kite_switch_ms) or TASK_COMBAT_KITE_SWITCH_MS
    )
    local arrive_distance = math.max(
        TASK_COMBAT_KITE_POINT_ARRIVE_DISTANCE,
        tonumber(state.task_combat_kite_arrive_distance) or TASK_COMBAT_KITE_POINT_ARRIVE_DISTANCE
    )
    local move_interval_ms = math.max(
        120,
        tonumber(state.task_combat_kite_move_interval_ms) or math.min(MOVE_INTERVAL_MS, TASK_COMBAT_KITE_SWITCH_MS)
    )
    local route_points = state.task_combat_kite_points
    local expected_points = configured_mode and configured_count or 4
    local route_needs_rebuild = type(route_points) ~= "table" or #route_points ~= expected_points

    if route_needs_rebuild then
        if configured_mode then
            route_points = {}
            for _, point in ipairs(configured_template) do
                route_points[#route_points + 1] = {
                    x = tonumber(point.x),
                    y = tonumber(point.y),
                    z = tonumber(point.z) or anchor_z,
                    source = "task_combat_kite",
                    path_index = #route_points + 1,
                    path_points = configured_count
                }
            end
        else
            route_points = build_task_combat_kite_points(anchor_x, anchor_y, anchor_z, radius)
        end
        state.task_combat_kite_points = route_points
        state.task_combat_kite_force_move = true
        state.task_combat_kite_anchor_route_x = anchor_x
        state.task_combat_kite_anchor_route_y = anchor_y
        state.task_combat_kite_anchor_route_z = anchor_z
        if type(route_points[1]) == "table" then
            local route_parts = {}
            for index, point in ipairs(route_points) do
                route_parts[#route_parts + 1] = string.format(
                    "p%d=%.2f, %.2f",
                    index,
                    tonumber(point.x) or 0,
                    tonumber(point.y) or 0
                )
            end
            logger(state.log_ctx).info(string.format(
                "[Leveling] kite route built | task=%s mode=%s center=%.2f, %.2f, %.2f radius=%.2f points=%d start_index=%d switch_ms=%d move_interval_ms=%d arrive_distance=%.2f %s",
                tostring(M.current_task_log_name() or ""),
                configured_mode and "configured" or "generated",
                tonumber(anchor_x) or 0,
                tonumber(anchor_y) or 0,
                tonumber(anchor_z) or 0,
                tonumber(radius) or 0,
                #route_points,
                configured_mode and 1 or 0,
                configured_mode and configured_switch_ms or generated_switch_ms,
                move_interval_ms,
                arrive_distance,
                table.concat(route_parts, " ")
            ))
        end

        if configured_mode then
            state.task_combat_kite_index = 1
            state.task_combat_next_kite_switch_at = current_time + configured_switch_ms
        else
            local nearest_index = 1
            local nearest_distance = math.huge
            for index, point in ipairs(route_points) do
                local point_distance = distance_2d(player_x, player_y, point.x, point.y)
                if point_distance < nearest_distance then
                    nearest_distance = point_distance
                    nearest_index = index
                end
            end
            state.task_combat_kite_index = nearest_index
            state.task_combat_next_kite_switch_at = current_time + generated_switch_ms
        end
    end

    local current_index = tonumber(state.task_combat_kite_index) or 1
    if current_index < 1 or current_index > #route_points then
        current_index = 1
    end

    local current_point = route_points[current_index]
    if type(current_point) ~= "table" then
        current_index = 1
        current_point = route_points[current_index]
    end
    if type(current_point) ~= "table" then
        return nil
    end

    local current_distance = distance_2d(player_x, player_y, current_point.x, current_point.y)
    local point_too_far = current_distance >= math.max(
        2400,
        radius * 1.55
    )
    local should_advance = nil
    if configured_mode then
        should_advance = current_distance <= arrive_distance
            or current_time >= (tonumber(state.task_combat_next_kite_switch_at) or 0)
    else
        should_advance = current_distance <= arrive_distance
            or point_too_far
            or current_time >= (tonumber(state.task_combat_next_kite_switch_at) or 0)
    end
    if should_advance then
        current_index = current_index + 1
        if current_index > #route_points then
            current_index = 1
        end
        state.task_combat_kite_index = current_index
        state.task_combat_kite_force_move = true
        state.task_combat_next_kite_switch_at = current_time + (configured_mode and configured_switch_ms or generated_switch_ms)
        current_point = route_points[current_index]
        current_distance = distance_2d(player_x, player_y, current_point.x, current_point.y)
    else
        state.task_combat_kite_index = current_index
    end
    local force_move = state.task_combat_kite_force_move == true
    state.task_combat_kite_force_move = false

    return {
        x = current_point.x,
        y = current_point.y,
        z = current_point.z,
        source = "task_combat_kite",
        path_index = current_index,
        path_points = #route_points,
        current_distance = current_distance,
        move_interval_ms = move_interval_ms,
        force_move = force_move
    }
end

function M.publish_task_combat_kite_route_worker(ctx, current_time, kite_target)
    if state.task_combat_kite_async_worker ~= true then
        return false, "task combat kite async route worker disabled."
    end
    local route_points = state.task_combat_kite_points
    if type(route_points) ~= "table" or #route_points < 2 then
        return false, "task combat kite route points unavailable."
    end

    current_time = tonumber(current_time) or now_ms(ctx)
    local worker_ok, worker_err = ensure_nav_worker_running(ctx, current_time, true)
    if not worker_ok then
        return false, worker_err
    end

    local move_interval_ms = math.max(
        80,
        tonumber(kite_target and kite_target.move_interval_ms)
            or tonumber(state.task_combat_kite_move_interval_ms)
            or math.min(MOVE_INTERVAL_MS, TASK_COMBAT_KITE_SWITCH_MS)
    )
    local arrive_distance = math.max(
        80,
        tonumber(state.task_combat_kite_arrive_distance) or TASK_COMBAT_KITE_POINT_ARRIVE_DISTANCE
    )
    local switch_ms = math.max(
        300,
        tonumber(state.task_combat_kite_switch_ms) or TASK_COMBAT_KITE_SWITCH_MS
    )

    local parts = {
        tostring(#route_points),
        tostring(math.floor(move_interval_ms + 0.5)),
        tostring(math.floor(arrive_distance + 0.5)),
        tostring(math.floor(switch_ms + 0.5))
    }
    for index, point in ipairs(route_points) do
        parts[#parts + 1] = string.format(
            "%d:%.1f:%.1f:%.1f",
            index,
            tonumber(point.x) or 0,
            tonumber(point.y) or 0,
            tonumber(point.z) or 0
        )
    end
    local signature = table.concat(parts, "|")
    if signature ~= tostring(state.task_combat_kite_route_worker_signature or "") then
        state.task_combat_kite_route_worker_version = (tonumber(state.task_combat_kite_route_worker_version) or 0) + 1
        state.task_combat_kite_route_worker_signature = signature

        for index, point in ipairs(route_points) do
            nav_worker_set(ctx, "route_point_" .. index .. "_x", tonumber(point.x) or 0)
            nav_worker_set(ctx, "route_point_" .. index .. "_y", tonumber(point.y) or 0)
            nav_worker_set(ctx, "route_point_" .. index .. "_z", tonumber(point.z) or 0)
        end
        nav_worker_set(ctx, "route_count", #route_points)
        nav_worker_set(ctx, "route_arrive_distance", arrive_distance)
        nav_worker_set(ctx, "route_switch_ms", switch_ms)
        nav_worker_set(ctx, "target_source", "task_combat_kite")
        nav_worker_set(ctx, "target_path_index", tonumber(kite_target and kite_target.path_index) or 0)
        nav_worker_set(ctx, "move_interval_ms", move_interval_ms)
        nav_worker_set(ctx, "mode", "route_loop")
        nav_worker_set(ctx, "route_version", tonumber(state.task_combat_kite_route_worker_version) or 1)
        logger(ctx).info(string.format(
            "[Leveling] async kite route worker armed | points=%d move_interval_ms=%d arrive_distance=%.2f switch_ms=%d version=%d",
            #route_points,
            tonumber(move_interval_ms) or 0,
            tonumber(arrive_distance) or 0,
            tonumber(switch_ms) or 0,
            tonumber(state.task_combat_kite_route_worker_version) or 0
        ))
    else
        nav_worker_set(ctx, "target_source", "task_combat_kite")
        nav_worker_set(ctx, "move_interval_ms", move_interval_ms)
        nav_worker_set(ctx, "mode", "route_loop")
    end

    nav_worker_set(ctx, "paused", false)
    nav_worker_set(ctx, "stop", false)
    state.nav_worker_paused = false
    state.task_combat_kite_route_worker_active = true
    state.next_move_at = current_time + move_interval_ms
    return true
end

function M.issue_task_combat_kite_move(ctx, current_time, kite_target)
    local worker_ok, worker_err = M.publish_task_combat_kite_route_worker(ctx, current_time, kite_target)
    if worker_ok then
        return true
    end
    if state.task_combat_kite_async_worker == true then
        log_throttled(ctx, "async_kite_worker_fallback", "warn", LOG_THROTTLE_MS,
            "[Leveling] async kite route worker unavailable, fallback to direct MoveTo: " .. tostring(worker_err))
    end
    return issue_move(ctx, current_time, kite_target)
end

M.interact_with_npc = function(ctx, current_time, npc, opts)
    if current_time - (tonumber(state.last_dialogue_at) or 0) < DIALOGUE_COOLDOWN_MS then
        return false, "dialogue cooldown."
    end

    release_async_combat_inputs(ctx, current_time, true)

    local ok, err = press_keyboard_hotkey(ctx, current_time, VK_D, "leveling npc dialogue")

    if not ok then
        log_throttled(ctx, "npc_dialogue_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] npc dialogue failed: " .. tostring(err))
        return false, err
    end

    arm_dialogue_followup(current_time, "npc", npc and npc.label or "", false)
    M.arm_npc_dialogue_combat_retry(current_time, npc, opts)
    logger(ctx).info(string.format(
        "[Leveling] npc dialogue triggered | npc=%s player_distance=%.2f task_distance=%.2f target_source=%s pos=%.2f, %.2f, %.2f esc_followup=disabled settle_ms=%d combat_retry_source=%s",
        tostring(npc and npc.label or ""),
        tonumber(npc and npc.distance) or 0,
        tonumber(npc and npc.task_distance) or 0,
        tostring(npc and npc.destination_source or ""),
        tonumber(npc and npc.x) or 0,
        tonumber(npc and npc.y) or 0,
        tonumber(npc and npc.z) or 0,
        POST_DIALOGUE_SETTLE_MS,
        tostring(state.npc_dialogue_combat_retry_source or "")
    ))
    return true
end

M.interact_with_prompt = function(ctx, current_time, prompt, goal_distance, target_source, refresh_on_timeout, allow_escape_followup)
    if current_time - (tonumber(state.last_dialogue_at) or 0) < DIALOGUE_COOLDOWN_MS then
        return false, "dialogue cooldown."
    end

    release_async_combat_inputs(ctx, current_time, true)

    local ok, err = press_keyboard_hotkey(ctx, current_time, VK_D, "leveling interaction prompt")
    if not ok then
        log_throttled(ctx, "interaction_prompt_failed", "warn", LOG_THROTTLE_MS,
            "[Leveling] interaction prompt failed: " .. tostring(err))
        return false, err
    end

    state.last_dialogue_at = current_time
    local prompt_label = prompt and (prompt.related_text or prompt.text or prompt.name) or INTERACTION_PROMPT_STEP.label
    local escape_enabled = false
    arm_dialogue_followup(
        current_time,
        "interaction_prompt",
        prompt_label,
        refresh_on_timeout ~= false
    )
    logger(ctx).info(string.format(
        "[Leveling] interaction prompt triggered | label=%s goal_distance=%.2f target_source=%s prompt_pos=%.2f, %.2f esc_followup=%s settle_ms=%d",
        tostring(prompt_label),
        tonumber(goal_distance) or 0,
        tostring(target_source or ""),
        tonumber(prompt and prompt.x) or 0,
        tonumber(prompt and prompt.y) or 0,
        escape_enabled and "enabled" or "disabled",
        POST_DIALOGUE_SETTLE_MS
    ))
    return true
end

local function press_escape_after_dialogue(ctx, current_time)
    state.dialogue_escape_due_at = 0
    state.dialogue_confirm_deadline_at = 0
    state.dialogue_ui_confirmed = false
    state.dialogue_ui_match = nil
    clear_pending_interaction()
    log_throttled(ctx, "dialogue_escape_disabled", "info", LOG_THROTTLE_MS,
        "[Leveling] dialogue ESC path disabled; only skip/jump button actions are allowed.")
    return false, "dialogue ESC disabled"
end

local function update_progress_anchor(current_time, player_x, player_y)
    if player_x == nil or player_y == nil then
        return
    end

    local position_change_reset_distance = math.max(8, math.min(PROGRESS_RESET_DISTANCE, 16))
    if state.last_position_change_x == nil or state.last_position_change_y == nil then
        state.last_position_change_x = player_x
        state.last_position_change_y = player_y
        state.last_position_change_at = current_time
    elseif distance_2d(player_x, player_y, state.last_position_change_x, state.last_position_change_y)
        >= position_change_reset_distance
    then
        state.last_position_change_x = player_x
        state.last_position_change_y = player_y
        state.last_position_change_at = current_time
    end

    if state.last_progress_x == nil or state.last_progress_y == nil then
        state.last_progress_x = player_x
        state.last_progress_y = player_y
        state.last_progress_at = current_time
        state.stall_retry_count = 0
        return
    end

    if distance_2d(player_x, player_y, state.last_progress_x, state.last_progress_y) >= PROGRESS_RESET_DISTANCE then
        state.last_progress_x = player_x
        state.last_progress_y = player_y
        state.last_progress_at = current_time
        state.stall_retry_count = 0
        state.last_move_failure_at = 0
        state.move_failure_streak = 0
    end
end

log_heartbeat = function(ctx, current_time, player_x, player_y, player_z)
    if current_time - (tonumber(state.last_heartbeat_at) or 0) < HEARTBEAT_INTERVAL_MS then
        return
    end

    state.last_heartbeat_at = current_time
    if player_x ~= nil and player_y ~= nil then
        logger(ctx).info(string.format(
            "[Leveling] heartbeat | stage=%s tick=%d pos=%.2f, %.2f, %.2f target=%s path_points=%d",
            tostring(state.stage),
            tonumber(state.ticks) or 0,
            tonumber(player_x) or 0,
            tonumber(player_y) or 0,
            tonumber(player_z) or 0,
            type(state.task_target) == "table"
                and string.format("%.2f, %.2f", tonumber(state.task_target.x) or 0, tonumber(state.task_target.y) or 0)
                or "nil",
            tonumber(state.task_path_count) or 0
        ))
    else
        logger(ctx).info(string.format(
            "[Leveling] heartbeat | stage=%s tick=%d pos=unavailable target=%s path_points=%d",
            tostring(state.stage),
            tonumber(state.ticks) or 0,
            type(state.task_target) == "table"
                and string.format("%.2f, %.2f", tonumber(state.task_target.x) or 0, tonumber(state.task_target.y) or 0)
                or "nil",
            tonumber(state.task_path_count) or 0
        ))
    end
end

function M.log_execution_trace(ctx, current_time, reason, target, destination, goal_distance, objective_mode, player_x, player_y, player_z)
    local stage_name = tostring(state.stage or "")
    local task_name = tostring(M.current_task_log_name() or state.current_task_name or "")
    local task_detail = tostring(M.current_task_log_detail() or state.current_task_detail or "")
    local target_source = type(target) == "table" and tostring(target.source or "") or ""
    local target_text = type(target) == "table"
        and string.format("%.2f, %.2f, %.2f", tonumber(target.x) or 0, tonumber(target.y) or 0, tonumber(target.z) or 0)
        or "nil"
    local destination_text = is_valid_world_point(destination)
        and string.format("%.2f, %.2f, %.2f", tonumber(destination.x) or 0, tonumber(destination.y) or 0, tonumber(destination.z) or 0)
        or "nil"
    local pos_text = player_x ~= nil and player_y ~= nil
        and string.format("%.2f, %.2f, %.2f", tonumber(player_x) or 0, tonumber(player_y) or 0, tonumber(player_z) or 0)
        or "nil"
    local goal_text = type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil"
    local mode_text = tostring(objective_mode or "")
    local trace_key = table.concat({
        stage_name,
        tostring(reason or ""),
        task_name,
        task_detail,
        target_source,
        target_text,
        destination_text,
        goal_text,
        mode_text,
        pos_text
    }, "|")

    if trace_key == state.last_exec_trace_key
        and current_time - (tonumber(state.last_exec_trace_at) or 0) < 1000
    then
        return
    end

    state.last_exec_trace_key = trace_key
    state.last_exec_trace_at = current_time
    logger(ctx).info(string.format(
        "[Leveling] exec trace | stage=%s reason=%s task=%s detail=%s pos=%s target_source=%s target=%s destination=%s goal_distance=%s objective_mode=%s",
        stage_name,
        tostring(reason or ""),
        task_name,
        task_detail,
        pos_text,
        target_source,
        target_text,
        destination_text,
        goal_text,
        mode_text
    ))
end

function M.start(ctx)
    reset_state()

    local current_time = now_ms(ctx)
    local ok, err = ensure_nav_ready(ctx, current_time)
    if not ok then
        reset_state()
        return false, err
    end

    if LEVELING_USE_NAV_WORKER == true then
        local worker_ok, worker_err = start_nav_worker(ctx, current_time)
        if not worker_ok then
            log_throttled(ctx, "nav_worker_start_failed", "warn", LOG_THROTTLE_MS,
                "[Leveling] nav worker start failed, direct MoveTo fallback enabled: " .. tostring(worker_err))
        else
            logger(ctx).info(string.format(
                "[Leveling] navigation mode | async nav worker path-route follow | task_move_interval=%dms task_pos_interval=%dms",
                tonumber(M.TASK_FOLLOW_MOVE_INTERVAL_MS) or 0,
                tonumber(M.TASK_POS_MOVE_INTERVAL_MS) or 0
            ))
        end
    else
        state.nav_worker_force_direct = true
        logger(ctx).info(string.format(
            "[Leveling] navigation mode | direct MoveTo route-style follow | task_move_interval=%dms task_pos_interval=%dms",
            tonumber(M.TASK_FOLLOW_MOVE_INTERVAL_MS) or 0,
            tonumber(M.TASK_POS_MOVE_INTERVAL_MS) or 0
        ))
    end

    state.running = true
    local restored_treasure_resume = false
    local startup_player_x, startup_player_y, startup_player_z = read_player_pos(ctx)
    if type(M._leveling_treasure) == "table"
        and type(M._leveling_treasure.restore_resume_snapshot) == "function"
        and type(M.TREASURE_DUNGEON_CONFIGS) == "table"
        and #M.TREASURE_DUNGEON_CONFIGS > 0
    then
        restored_treasure_resume = M._leveling_treasure.restore_resume_snapshot(
            ctx,
            state,
            M.TREASURE_DUNGEON_CONFIGS,
            startup_player_x,
            startup_player_y,
            startup_player_z
        ) == true
    end
    state.log_ctx = ctx
    state.stage = "bootstrap"
    state.next_tick_at = 0
    state.next_move_at = current_time
    state.next_action_at = current_time
    state.next_task_refresh_at = current_time
    state.next_task_name_probe_at = current_time
    state.next_task_button_click_at = current_time
    state.next_follow_task_button_refresh_at = current_time + TASK_BUTTON_KEEPALIVE_INTERVAL_MS
    state.last_progress_at = current_time
    state.startup_boss_engage_until = current_time + 10000
    state.startup_state_resolve_until = current_time + 3000
    state.startup_main_task_reacquired = false
    if restored_treasure_resume then
        state.startup_boss_engage_until = 0
        state.startup_state_resolve_until = 0
        state.startup_main_task_reacquired = true
        logger(ctx).info("[Leveling] treasure resume restored, startup probing disabled for direct resume.")
    end

    logger(ctx).info("[Leveling] runner started | mode=2 quest button -> task path -> NPC dialogue")
    logger(ctx).info("[Leveling] logic: click main task button, fetch task path, MoveTo target, confirm nearby NPC, press D, then continue")
    logger(ctx).info("[Leveling] startup boss combat window armed | window=10000ms")
    logger(ctx).info("[Leveling] startup state resolve armed | window=3000ms")
    logger(ctx).info(string.format(
        "[Leveling] map runtime detection | enabled=%s",
        should_use_map_runtime_detection() and "true" or "false"
    ))
    return true
end

function M.maybe_handle_forced_kite_monster(ctx, current_time, player_x, player_y, player_z, hp_ratio, target, destination, goal_distance)
    local task_monsters, monster_err = M.find_task_monsters(ctx, current_time, player_x, player_y)
    local forced_monster = M.find_forced_kite_monster(task_monsters)
    local map_cfg = select(1, current_map_task_config())
    local task_cfg, task_name = M.current_task_runtime_config()
    local point_objective_cfg = M.current_objective_point_config(destination, target)
    local objective_cfg = type(task_cfg) == "table" and type(task_cfg.objective) == "table" and task_cfg.objective
        or (type(map_cfg) == "table" and type(map_cfg.objective) == "table" and map_cfg.objective or nil)
        or point_objective_cfg
    local objective_source = type(task_cfg) == "table" and type(task_cfg.objective) == "table" and "task"
        or (type(map_cfg) == "table" and type(map_cfg.objective) == "table" and "map" or nil)
        or (type(point_objective_cfg) == "table" and "point" or "none")
    local objective_mode = type(objective_cfg) == "table" and tostring(objective_cfg.mode or "") or ""
    local boss_objective_active = objective_mode == "boss_kite"
    local revive_boss_engage_armed = boss_objective_active
        and (tonumber(state.post_revive_boss_engage_until) or 0) > current_time
    local startup_boss_engage_armed = boss_objective_active
        and (tonumber(state.startup_boss_engage_until) or 0) > current_time
    local no_target_boss_engage_armed = boss_objective_active
        and type(objective_cfg) == "table"
        and objective_cfg.allow_no_task_target_force_kite == true
        and type(target) ~= "table"
        and not is_valid_world_point(destination)
    local boss_objective_trigger_distance = math.max(
        tonumber(type(objective_cfg) == "table" and objective_cfg.trigger_distance) or TARGET_REACHED_DISTANCE,
        760
    )
    local startup_boss_far_goal_threshold = math.max(
        boss_objective_trigger_distance * 2,
        1500
    )
    local startup_boss_following_far_goal = startup_boss_engage_armed
        and type(goal_distance) == "number"
        and goal_distance > startup_boss_far_goal_threshold
        and (type(target) == "table" or is_valid_world_point(destination))
    if startup_boss_following_far_goal then
        log_throttled(ctx, "startup_boss_engage_deferred", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] startup boss engage deferred, continue route follow | task=%s goal_distance=%.2f threshold=%.2f target_source=%s path_points=%d",
            tostring(select(2, M.current_task_runtime_config()) or state.current_task_name or ""),
            goal_distance,
            startup_boss_far_goal_threshold,
            tostring(target and target.source or destination and destination.source or "none"),
            tonumber(state.task_path_count) or 0
        ))
        state.startup_boss_engage_until = 0
        startup_boss_engage_armed = false
    end
    local early_boss_monster = nil
    if type(task_monsters) == "table"
        and boss_objective_active
        and tonumber(task_monsters.count) and tonumber(task_monsters.count) > 0
        and type(goal_distance) == "number"
        and goal_distance <= boss_objective_trigger_distance
    then
        early_boss_monster = task_monsters.nearest_special or task_monsters.nearest or nil
    end
    local revive_boss_monster = nil
    if type(task_monsters) == "table"
        and revive_boss_engage_armed
        and tonumber(task_monsters.count) and tonumber(task_monsters.count) > 0
    then
        revive_boss_monster = task_monsters.nearest_special or task_monsters.nearest or nil
    end
    local startup_boss_monster = nil
    if type(task_monsters) == "table"
        and startup_boss_engage_armed
        and tonumber(task_monsters.count) and tonumber(task_monsters.count) > 0
    then
        startup_boss_monster = task_monsters.nearest_special or task_monsters.nearest or nil
    end
    local no_target_boss_monster = nil
    if type(task_monsters) == "table"
        and no_target_boss_engage_armed
        and tonumber(task_monsters.count) and tonumber(task_monsters.count) > 0
    then
        no_target_boss_monster = task_monsters.nearest_special or task_monsters.nearest or nil
    end
    local sticky_forced_kite = state.task_combat_force_kite == true
        and (tonumber(state.task_combat_last_seen_at) or 0) > 0
        and current_time - (tonumber(state.task_combat_last_seen_at) or 0) <= TASK_COMBAT_CLEAR_SETTLE_MS
        and tonumber(state.task_combat_anchor_x) ~= nil
        and tonumber(state.task_combat_anchor_y) ~= nil

    if type(forced_monster) ~= "table"
        and type(revive_boss_monster) ~= "table"
        and type(startup_boss_monster) ~= "table"
        and type(early_boss_monster) ~= "table"
        and type(no_target_boss_monster) ~= "table"
        and not sticky_forced_kite
    then
        if revive_boss_engage_armed then
            log_throttled(ctx, "revive_boss_engage_wait", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] revive boss combat armed, waiting monster visibility | task=%s window_left=%dms monster_err=%s",
                tostring(state.current_task_name or ""),
                math.max(0, (tonumber(state.post_revive_boss_engage_until) or current_time) - current_time),
                tostring(monster_err or "")
            ))
        elseif startup_boss_engage_armed then
            log_throttled(ctx, "startup_boss_engage_wait", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] startup boss combat armed, waiting monster visibility | task=%s window_left=%dms monster_err=%s",
                tostring(state.current_task_name or ""),
                math.max(0, (tonumber(state.startup_boss_engage_until) or current_time) - current_time),
                tostring(monster_err or "")
            ))
        elseif no_target_boss_engage_armed then
            log_throttled(ctx, "no_target_boss_engage_wait", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] no-target boss combat armed, waiting monster visibility | task=%s detail=%s monster_err=%s",
                tostring(task_name or state.current_task_name or ""),
                tostring(M.current_task_log_detail() or state.current_task_detail or ""),
                tostring(monster_err or "")
            ))
        end
        return false
    end

    local selected_monster = forced_monster or revive_boss_monster or startup_boss_monster or early_boss_monster or no_target_boss_monster

    if type(selected_monster) == "table" then
        local matched_name = tostring(selected_monster.force_kite_name or selected_monster.label or "")
        local engage_reason = type(forced_monster) == "table" and "forced_name"
            or (type(revive_boss_monster) == "table" and "post_revive_boss_room"
            or (type(startup_boss_monster) == "table" and "startup_boss_room"
            or (type(no_target_boss_monster) == "table" and "no_task_target_boss_room" or "boss_objective_early")))
        log_throttled(ctx, "forced_kite_monster_match", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] forced kite monster matched | task=%s detail=%s reason=%s label=%s player_distance=%s task_distance=%s goal_distance=%s sticky=%s objective_source=%s objective_key=%s objective_mode=%s",
            tostring(task_name or state.current_task_name or ""),
            tostring(M.current_task_log_detail() or state.current_task_detail or ""),
            engage_reason,
            matched_name,
            tonumber(selected_monster.distance) ~= nil and string.format("%.2f", tonumber(selected_monster.distance) or 0) or "nil",
            tonumber(selected_monster.task_distance) ~= nil and string.format("%.2f", tonumber(selected_monster.task_distance) or 0) or "nil",
            tonumber(goal_distance) ~= nil and string.format("%.2f", tonumber(goal_distance) or 0) or "nil",
            sticky_forced_kite and "true" or "false",
            tostring(objective_source or "none"),
            tostring(type(objective_cfg) == "table" and objective_cfg.key or ""),
            tostring(objective_mode or "")
        ))
        state.task_combat_force_kite = true
        M.lock_boss_combat_context(task_name, objective_cfg, point_objective_cfg)
        M.mark_task_combat_seen(current_time, player_x, player_y, destination or target, task_monsters)
        state.task_combat_kite_template_points = nil
        state.task_combat_kite_switch_ms = nil
        M.apply_task_combat_kite_runtime_options(objective_cfg, point_objective_cfg)
        local configured_kite_points = type(point_objective_cfg) == "table" and point_objective_cfg.kite_points
            or type(objective_cfg) == "table" and objective_cfg.kite_points
            or nil
        if type(configured_kite_points) == "table" then
            local normalized_points = {}
            for _, point in ipairs(configured_kite_points) do
                local point_x = tonumber(type(point) == "table" and point.x)
                local point_y = tonumber(type(point) == "table" and point.y)
                if point_x ~= nil and point_y ~= nil then
                    normalized_points[#normalized_points + 1] = {
                        x = point_x,
                        y = point_y,
                        z = tonumber(type(point) == "table" and point.z) or tonumber(state.task_combat_anchor_z)
                    }
                end
            end
            if #normalized_points >= 3 then
                state.task_combat_kite_template_points = normalized_points
                state.task_combat_kite_switch_ms = tonumber(type(point_objective_cfg) == "table" and point_objective_cfg.kite_switch_ms)
                    or tonumber(type(objective_cfg) == "table" and objective_cfg.kite_switch_ms)
            end
        end
        if type(state.task_combat_kite_points) ~= "table" then
            if type(point_objective_cfg) == "table"
                and tostring(point_objective_cfg.mode or "") == "boss_kite"
                and tonumber(point_objective_cfg.x) ~= nil
                and tonumber(point_objective_cfg.y) ~= nil
            then
                state.task_combat_anchor_x = tonumber(point_objective_cfg.x)
                state.task_combat_anchor_y = tonumber(point_objective_cfg.y)
                state.task_combat_anchor_z = tonumber(point_objective_cfg.z) or tonumber(state.task_combat_anchor_z)
                state.task_combat_kite_radius = tonumber(point_objective_cfg.kite_radius)
                    or tonumber(objective_cfg and objective_cfg.kite_radius)
                    or tonumber(state.task_combat_kite_radius)
            else
                state.task_combat_anchor_x = tonumber(selected_monster.x) or tonumber(state.task_combat_anchor_x)
                state.task_combat_anchor_y = tonumber(selected_monster.y) or tonumber(state.task_combat_anchor_y)
                state.task_combat_anchor_z = tonumber(selected_monster.z) or tonumber(state.task_combat_anchor_z)
                state.task_combat_kite_radius = tonumber(objective_cfg and objective_cfg.kite_radius)
                    or tonumber(state.task_combat_kite_radius)
            end
        end
        state.post_revive_boss_engage_until = 0
        state.startup_boss_engage_until = 0
    else
        log_throttled(ctx, "forced_kite_monster_sticky", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] forced kite monster sticky continue | last_seen_ms=%d goal_distance=%s anchor=%.2f, %.2f",
            math.max(0, current_time - (tonumber(state.task_combat_last_seen_at) or 0)),
            tonumber(goal_distance) ~= nil and string.format("%.2f", tonumber(goal_distance) or 0) or "nil",
            tonumber(state.task_combat_anchor_x) or 0,
            tonumber(state.task_combat_anchor_y) or 0
        ))
    end

    local kite_target = M.build_task_combat_kite_target(current_time, player_x, player_y, destination or target)
    if type(kite_target) ~= "table" then
        log_throttled(ctx, "forced_kite_monster_target_missing", "warn", LOG_THROTTLE_MS, string.format(
            "[Leveling] forced kite monster matched but kite target unavailable | label=%s monster_err=%s",
            tostring(selected_monster and selected_monster.label or ""),
            tostring(monster_err or "")
        ))
        return false
    end

    state.task_reached_unresolved_since = 0
    state.stage = "task_combat_kite"
    M.issue_task_combat_kite_move(ctx, current_time, kite_target)
    issue_combat_pulse(ctx, current_time, "forced_kite_monster", true)
    log_throttled(ctx, "forced_kite_monster_execute", "info", LOG_THROTTLE_MS, string.format(
        "[Leveling] forced kite combat engaged | task=%s detail=%s label=%s sticky=%s objective_source=%s objective_key=%s objective_mode=%s anchor=%.2f, %.2f kite_target=%.2f, %.2f route_index=%d/%d hp_ratio=%s",
        tostring(task_name or state.current_task_name or ""),
        tostring(M.current_task_log_detail() or state.current_task_detail or ""),
        tostring(selected_monster and selected_monster.label or ""),
        sticky_forced_kite and "true" or "false",
        tostring(objective_source or "none"),
        tostring(type(objective_cfg) == "table" and objective_cfg.key or ""),
        tostring(objective_mode or ""),
        tonumber(state.task_combat_anchor_x) or 0,
        tonumber(state.task_combat_anchor_y) or 0,
        tonumber(kite_target.x) or 0,
        tonumber(kite_target.y) or 0,
        tonumber(kite_target.path_index) or 0,
        tonumber(kite_target.path_points) or 0,
        type(hp_ratio) == "number" and string.format("%.2f", tonumber(hp_ratio) or 0) or "nil"
    ))
    log_heartbeat(ctx, current_time, player_x, player_y, player_z)
    return true
end

function M.maybe_handle_startup_state(ctx, current_time, player_x, player_y, player_z, hp_ratio)
    local resolve_until = tonumber(state.startup_state_resolve_until) or 0
    if resolve_until <= 0 then
        return false
    end

    if current_time > resolve_until then
        state.startup_state_resolve_until = 0
        logger(ctx).info("[Leveling] startup state resolve expired, fallback to normal task flow.")
        return false
    end

    if current_time >= (tonumber(state.next_task_name_probe_at) or 0) then
        local hint_x, hint_y = resolve_main_task_button_hint(ctx)
        if hint_x ~= nil and hint_y ~= nil then
            M.refresh_current_task_name(ctx, current_time, nil, hint_x, hint_y)
        end
        state.next_task_name_probe_at = current_time + 600
    end

    if current_time >= (tonumber(state.next_task_refresh_at) or 0) then
        update_task_target(ctx, current_time, player_x, player_y)
    end

    local target = state.task_target
    local destination = build_task_destination_point(player_x, player_y)
    local target_distance = type(target) == "table" and distance_2d(player_x, player_y, target.x, target.y) or nil
    local destination_distance = destination
        and distance_2d(player_x, player_y, destination.x, destination.y)
        or nil
    local goal_distance = destination_distance or target_distance
    local map_cfg = select(1, current_map_task_config())
    local task_cfg, task_name = M.current_task_runtime_config()
    local point_objective_cfg = M.current_objective_point_config(destination, target)
    local objective_cfg = type(task_cfg) == "table" and type(task_cfg.objective) == "table" and task_cfg.objective
        or (type(map_cfg) == "table" and type(map_cfg.objective) == "table" and map_cfg.objective or nil)
        or point_objective_cfg
    local objective_mode = type(objective_cfg) == "table" and tostring(objective_cfg.mode or "") or ""
    local objective_ready_distance = type(M._leveling_policy) == "table"
        and type(M._leveling_policy.objective_ready_distance) == "function"
        and M._leveling_policy.objective_ready_distance(TARGET_REACHED_DISTANCE, objective_cfg)
        or TARGET_REACHED_DISTANCE
    local window_left_ms = math.max(0, resolve_until - current_time)

    if M.maybe_handle_forced_kite_monster(
        ctx,
        current_time,
        player_x,
        player_y,
        player_z,
        hp_ratio,
        target,
        destination,
        goal_distance
    ) then
        state.startup_state_resolve_until = 0
        return true
    end

    if type(target) == "table"
        and tostring(target.source or "") == "task_pos"
    then
        if current_time >= (tonumber(state.next_task_button_click_at) or 0) then
            local clicked = click_main_task_button(ctx, current_time)
            if clicked then
                state.task_pos_reject_until = current_time + TASK_BUTTON_PATH_FETCH_TIMEOUT_MS + 2500
                state.task_pos_reject_reason = "startup_task_pos_requires_main_task_call"
                logger(ctx).info(string.format(
                    "[Leveling] startup task_pos requires main task call | task=%s detail=%s goal_distance=%s target=(%.2f, %.2f, %.2f) window_left=%dms",
                    tostring(task_name or state.current_task_name or ""),
                    tostring(M.current_task_log_detail() or state.current_task_detail or ""),
                    type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil",
                    tonumber(target.x) or 0,
                    tonumber(target.y) or 0,
                    tonumber(target.z) or 0,
                    window_left_ms
                ))
                state.stage = "wait_task_path_after_button"
                state.startup_state_resolve_until = 0
                release_async_combat_inputs(ctx, current_time, true)
                hold_navigation(ctx, current_time, "wait_task_path_after_button")
                log_heartbeat(ctx, current_time, player_x, player_y, player_z)
                return true
            end
        end

        state.stage = "startup_task_reacquire"
        release_async_combat_inputs(ctx, current_time, true)
        hold_navigation(ctx, current_time, "startup_task_reacquire")
        log_throttled(ctx, "startup_task_pos_wait_main_task_call", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] startup holding task_pos until main task call succeeds | task=%s detail=%s goal_distance=%s window_left=%dms",
            tostring(task_name or state.current_task_name or ""),
            tostring(M.current_task_log_detail() or state.current_task_detail or ""),
            type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil",
            window_left_ms
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if type(goal_distance) == "number"
        and goal_distance <= objective_ready_distance
        and (type(target) == "table" or is_valid_world_point(destination))
    then
        local objective_target = target
        if type(objective_target) ~= "table" and is_valid_world_point(destination) then
            objective_target = {
                x = tonumber(destination.x),
                y = tonumber(destination.y),
                z = tonumber(destination.z),
                source = tostring(destination.source or "startup_objective")
            }
        end
        if type(objective_target) == "table" then
            logger(ctx).info(string.format(
                "[Leveling] startup state resolved | action=objective task=%s source=%s goal_distance=%s",
                tostring(task_name or state.current_task_name or ""),
                tostring(objective_target.source or ""),
                type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil"
            ))
            state.startup_state_resolve_until = 0
            return M.maybe_handle_task_reached(
                ctx,
                current_time,
                player_x,
                player_y,
                player_z,
                hp_ratio,
                objective_target,
                destination,
                destination_distance,
                goal_distance
            )
        end
    end

    if type(target) == "table"
        and tostring(target.source or "") == "task_pos"
        and objective_mode == "boss_kite"
        and type(goal_distance) == "number"
        and goal_distance > math.max(1200, objective_ready_distance * 3)
    then
        logger(ctx).info(string.format(
            "[Leveling] startup stale boss task_pos ignored, fallback to normal task refresh | task=%s goal_distance=%.2f target=%.2f, %.2f source=%s",
            tostring(task_name or state.current_task_name or ""),
            goal_distance,
            tonumber(target.x) or 0,
            tonumber(target.y) or 0,
            tostring(target.source or "")
        ))
        clear_task_target_state()
        state.require_task_button_refresh = true
        state.require_task_button_refresh_reason = "startup_stale_boss_task_pos"
        state.next_task_button_click_at = current_time
        state.next_task_refresh_at = current_time
        state.startup_state_resolve_until = 0
        return false
    end

    local no_task_data = type(target) ~= "table"
        and not is_valid_world_point(state.task_pos)
        and not is_valid_world_point(destination)
    if type(target) == "table"
        and tostring(target.source or "") == "task_pos"
        and type(goal_distance) == "number"
        and goal_distance > math.max(objective_ready_distance, 220)
        and current_time >= (tonumber(state.next_task_button_click_at) or 0)
    then
        local clicked = click_main_task_button(ctx, current_time)
        if clicked then
            logger(ctx).info(string.format(
                "[Leveling] startup task_pos deferred, clicked main task before follow | task=%s detail=%s goal_distance=%.2f target=(%.2f, %.2f, %.2f) source=%s window_left=%dms",
                tostring(task_name or state.current_task_name or ""),
                tostring(M.current_task_log_detail() or state.current_task_detail or ""),
                goal_distance,
                tonumber(target.x) or 0,
                tonumber(target.y) or 0,
                tonumber(target.z) or 0,
                tostring(target.source or ""),
                window_left_ms
            ))
            state.stage = "wait_task_path_after_button"
            state.startup_state_resolve_until = 0
            release_async_combat_inputs(ctx, current_time, true)
            hold_navigation(ctx, current_time, "wait_task_path_after_button")
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end
    end

    local task_entry_action_active = M.is_task_entry_action_active(current_time)

    if no_task_data
        and current_time >= (tonumber(state.next_task_button_click_at) or 0)
        and task_entry_action_active ~= true
    then
        local clicked = click_main_task_button(ctx, current_time)
        if clicked then
            logger(ctx).info(string.format(
                "[Leveling] startup state unresolved, clicked main task to reacquire task path immediately | task=%s window_left=%dms",
                tostring(task_name or state.current_task_name or ""),
                window_left_ms
            ))
            state.stage = "wait_task_path_after_button"
            state.startup_state_resolve_until = 0
            release_async_combat_inputs(ctx, current_time, true)
            hold_navigation(ctx, current_time, "wait_task_path_after_button")
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end
    elseif no_task_data and task_entry_action_active == true then
        log_throttled(ctx, "startup_resolve_suppressed_by_task_entry_action", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] startup state unresolved but task entry action owns retry flow | task=%s stage=%s",
            tostring(task_name or state.current_task_name or ""),
            tostring(state.stage or "")
        ))
    end

    if type(target) == "table" then
        logger(ctx).info(string.format(
            "[Leveling] startup state resolved | action=follow task=%s target_source=%s goal_distance=%s",
            tostring(task_name or state.current_task_name or ""),
            tostring(target.source or ""),
            type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil"
        ))
        state.startup_state_resolve_until = 0
        return false
    end

    log_throttled(ctx, "startup_state_probe", "info", LOG_THROTTLE_MS, string.format(
        "[Leveling] startup state probing | task=%s detail=%s has_task_pos=%s has_target=%s target_source=%s goal_distance=%s objective_mode=%s window_left=%dms",
        tostring(task_name or state.current_task_name or ""),
        tostring(M.current_task_log_detail() or state.current_task_detail or ""),
        is_valid_world_point(state.task_pos) and "true" or "false",
        type(target) == "table" and "true" or "false",
        type(target) == "table" and tostring(target.source or "") or "",
        type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil",
        tostring(objective_mode or ""),
        math.max(0, resolve_until - current_time)
    ))
    return false
end

function M.normalize_post_combat_loot_cfg(objective_cfg)
    local raw = type(objective_cfg) == "table" and objective_cfg.post_combat_loot or nil
    if raw == true then
        raw = {}
    end
    if type(raw) ~= "table" or raw.enabled == false then
        return nil
    end

    local duration_ms = math.max(800, tonumber(raw.duration_ms or raw.min_duration_ms) or 3000)
    local max_duration_ms = tonumber(raw.max_duration_ms)
    if max_duration_ms == nil then
        max_duration_ms = duration_ms + 2500
    end
    max_duration_ms = math.max(duration_ms, max_duration_ms)

    return {
        duration_ms = duration_ms,
        max_duration_ms = max_duration_ms,
        press_interval_ms = math.max(200, tonumber(raw.press_interval_ms) or 450),
        empty_settle_ms = math.max(200, tonumber(raw.empty_settle_ms) or 900)
    }
end

function M.count_ground_items_for_loot(ctx)
    local nav_mod = nav_api(ctx)
    if type(nav_mod) ~= "table" or type(nav_mod.enum_ground_items) ~= "function" then
        return nil, "nav.enum_ground_items is unavailable."
    end

    local ok, items = safe_call(nav_mod.enum_ground_items)
    if not ok then
        return nil, tostring(items or "enum_ground_items failed.")
    end
    if type(items) ~= "table" then
        return nil, "enum_ground_items returned non-table."
    end

    local count = #items
    if count > 0 then
        return count
    end
    for _, _ in pairs(items) do
        count = count + 1
    end
    return count
end

function M.maybe_handle_post_combat_loot(ctx, current_time, player_x, player_y, player_z, objective_cfg)
    current_time = tonumber(current_time) or now_ms(ctx)
    local cfg = M.normalize_post_combat_loot_cfg(objective_cfg)
    local active_key = tostring(state.post_combat_loot_active_key or "")
    if type(cfg) ~= "table" and active_key == "" then
        return false
    end

    local objective_key = tostring(type(objective_cfg) == "table" and objective_cfg.key or active_key)
    if objective_key == "" then
        objective_key = "objective"
    end

    if active_key == "" or active_key ~= objective_key then
        if type(cfg) ~= "table" then
            return false
        end
        state.post_combat_loot_active_key = objective_key
        state.post_combat_loot_started_at = current_time
        state.post_combat_loot_next_press_at = current_time
        state.post_combat_loot_last_item_at = 0
        state.post_combat_loot_duration_ms = cfg.duration_ms
        state.post_combat_loot_max_duration_ms = cfg.max_duration_ms
        state.post_combat_loot_press_interval_ms = cfg.press_interval_ms
        state.post_combat_loot_empty_settle_ms = cfg.empty_settle_ms
        logger(ctx).info(string.format(
            "[Leveling] post-combat loot started | objective=%s duration_ms=%d max_duration_ms=%d press_interval_ms=%d",
            objective_key,
            tonumber(cfg.duration_ms) or 0,
            tonumber(cfg.max_duration_ms) or 0,
            tonumber(cfg.press_interval_ms) or 0
        ))
    end

    state.stage = "post_combat_loot"
    M.pause_task_combat_kite_route_worker(ctx)
    hold_navigation(ctx, current_time, "post_combat_loot")

    local item_count, item_err = M.count_ground_items_for_loot(ctx)
    if type(item_count) == "number" and item_count > 0 then
        state.post_combat_loot_last_item_at = current_time
    end

    local started_at = tonumber(state.post_combat_loot_started_at) or current_time
    local elapsed_ms = current_time - started_at
    local duration_ms = tonumber(state.post_combat_loot_duration_ms) or 3000
    local max_duration_ms = math.max(duration_ms, tonumber(state.post_combat_loot_max_duration_ms) or duration_ms)
    local empty_settle_ms = tonumber(state.post_combat_loot_empty_settle_ms) or 900
    local last_item_at = tonumber(state.post_combat_loot_last_item_at) or 0
    local empty_ms = last_item_at > 0 and (current_time - last_item_at) or elapsed_ms
    local no_items = type(item_count) ~= "number" or item_count <= 0

    if elapsed_ms >= max_duration_ms or (elapsed_ms >= duration_ms and no_items and empty_ms >= empty_settle_ms) then
        log_throttled(ctx, "post_combat_loot_finished", "info", 0, string.format(
            "[Leveling] post-combat loot finished | objective=%s elapsed_ms=%d item_count=%s item_err=%s",
            tostring(state.post_combat_loot_active_key or objective_key),
            tonumber(elapsed_ms) or 0,
            type(item_count) == "number" and tostring(item_count) or "nil",
            tostring(item_err or "")
        ))
        M.clear_post_combat_loot_state()
        return false
    end

    if current_time >= (tonumber(state.post_combat_loot_next_press_at) or 0) then
        local ok, err = press_keyboard_hotkey(ctx, current_time, M.VK_A, "leveling post-combat loot")
        state.post_combat_loot_next_press_at = current_time + (tonumber(state.post_combat_loot_press_interval_ms) or 450)
        log_throttled(ctx, "post_combat_loot_pulse", "info", 600, string.format(
            "[Leveling] post-combat loot pulse | objective=%s elapsed_ms=%d item_count=%s press_ok=%s err=%s pos=%.2f, %.2f, %.2f",
            tostring(state.post_combat_loot_active_key or objective_key),
            tonumber(elapsed_ms) or 0,
            type(item_count) == "number" and tostring(item_count) or "nil",
            ok and "true" or "false",
            tostring(err or item_err or ""),
            tonumber(player_x) or 0,
            tonumber(player_y) or 0,
            tonumber(player_z) or 0
        ))
    end

    return true
end

function M.maybe_handle_task_combat_completion(ctx, current_time, player_x, player_y, player_z, hp_ratio, target, destination, destination_distance, goal_distance, objective_ready_distance)
    local stage_name = tostring(state.stage or "")
    local combat_stage_active = stage_name == "task_combat"
        or stage_name == "task_combat_kite"
        or stage_name == "task_combat_settle"
        or stage_name == "post_combat_loot"
        or state.task_combat_force_kite == true
    if not combat_stage_active then
        return false
    end

    local map_cfg = select(1, current_map_task_config())
    local task_cfg, task_name = M.current_task_runtime_config()
    local point_objective_cfg = M.current_objective_point_config(destination, target)
    local objective_cfg = type(task_cfg) == "table" and type(task_cfg.objective) == "table" and task_cfg.objective
        or (type(map_cfg) == "table" and type(map_cfg.objective) == "table" and map_cfg.objective or nil)
        or point_objective_cfg
    local objective_mode = tostring(type(objective_cfg) == "table" and objective_cfg.mode or "")
    M.ensure_terminal_task_lock(task_name, objective_cfg)
    local boss_kite_objective = objective_mode == "boss_kite" or state.task_combat_force_kite == true
    local post_combat_loot_finished = false
    if stage_name == "post_combat_loot" then
        if M.maybe_handle_post_combat_loot(ctx, current_time, player_x, player_y, player_z, objective_cfg) then
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end
        post_combat_loot_finished = true
    end
    if boss_kite_objective and M.maybe_handle_boss_task_change(ctx, current_time) then
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end
    if M.maybe_handle_terminal_task_change(ctx, current_time) then
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end
    local generic_followup_refresh_ms = tonumber(type(objective_cfg) == "table" and objective_cfg.generic_followup_refresh_ms) or 0
    local generic_followup_requires_task_pos_only = not (
        type(objective_cfg) == "table" and objective_cfg.generic_followup_requires_task_pos_only == false
    )
    local generic_followup_require_no_special = not (
        type(objective_cfg) == "table" and objective_cfg.generic_followup_require_no_special == false
    )
    local defer_boss_followup_until_clear = type(objective_cfg) == "table"
        and objective_cfg.defer_followup_until_clear == true
    local target_source = tostring(target and target.source or destination and destination.source or "")
    local generic_followup_ready = generic_followup_refresh_ms > 0
        and (
            not generic_followup_requires_task_pos_only
            or target_source == "task_pos"
            or (tonumber(state.task_path_count) or 0) <= 0
        )
        and type(goal_distance) == "number"
        and goal_distance <= math.max(
            tonumber(objective_ready_distance) or TARGET_REACHED_DISTANCE,
            TARGET_REACHED_DISTANCE
        )

    local task_monsters, monster_err = M.find_task_monsters(ctx, current_time, player_x, player_y)
    if not post_combat_loot_finished
        and task_monsters
        and tonumber(task_monsters.count)
        and tonumber(task_monsters.count) > 0
    then
        M.mark_task_combat_seen(current_time, player_x, player_y, destination or target, task_monsters)
        local combat_alive_ms = math.max(
            0,
            current_time - (tonumber(state.task_combat_started_at) or current_time)
        )
        local has_special = type(task_monsters.nearest_special) == "table"
        if boss_kite_objective
            and not defer_boss_followup_until_clear
            and not has_special
            and combat_alive_ms >= 1800
        then
            if M.maybe_handle_task_objective_button(ctx, current_time, goal_distance, target_source, objective_cfg) then
                log_throttled(ctx, "boss_combat_followup_objective_button", "info", LOG_THROTTLE_MS, string.format(
                    "[Leveling] boss combat follow-up handled by objective button | task=%s combat_ms=%d monsters=%d goal_distance=%s objective_mode=%s",
                    tostring(task_name or state.current_task_name or ""),
                    tonumber(combat_alive_ms) or 0,
                    tonumber(task_monsters.count) or 0,
                    type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil",
                    tostring(objective_mode)
                ))
                return true
            end
            if M.maybe_handle_task_reached_prompt_or_portal(ctx, current_time, goal_distance, target_source) then
                log_throttled(ctx, "boss_combat_followup_prompt_or_portal", "info", LOG_THROTTLE_MS, string.format(
                    "[Leveling] boss combat follow-up handled by prompt/portal | task=%s combat_ms=%d monsters=%d goal_distance=%s objective_mode=%s",
                    tostring(task_name or state.current_task_name or ""),
                    tonumber(combat_alive_ms) or 0,
                    tonumber(task_monsters.count) or 0,
                    type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil",
                    tostring(objective_mode)
                ))
                return true
            end
            if M.maybe_click_global_task_portal(ctx, current_time, {
                phase = "boss_combat_followup",
                player_x = player_x,
                player_y = player_y,
                target = target,
                destination = destination,
                goal_distance = goal_distance
            }) then
                state.stage = "global_task_portal"
                log_throttled(ctx, "boss_combat_followup_global_portal", "info", LOG_THROTTLE_MS, string.format(
                    "[Leveling] boss combat follow-up handled by global portal | task=%s combat_ms=%d monsters=%d goal_distance=%s objective_mode=%s",
                    tostring(task_name or state.current_task_name or ""),
                    tonumber(combat_alive_ms) or 0,
                    tonumber(task_monsters.count) or 0,
                    type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil",
                    tostring(objective_mode)
                ))
                log_heartbeat(ctx, current_time, player_x, player_y, player_z)
                return true
            end
        end
        if boss_kite_objective then
            state.task_combat_force_kite = true
            local kite_target = M.build_task_combat_kite_target(current_time, player_x, player_y, destination or target)
            state.stage = "task_combat_kite"
            if type(kite_target) == "table" then
                M.issue_task_combat_kite_move(ctx, current_time, kite_target)
            end
            issue_combat_pulse(ctx, current_time, "boss_kite_maintain", true)
            log_throttled(ctx, "task_combat_boss_still_active", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] boss combat still active, keep kiting | task=%s source=%s combat_ms=%d monsters=%d has_special=%s goal_distance=%s objective_mode=%s force_kite=%s route_index=%s/%s",
                tostring(task_name or state.current_task_name or ""),
                tostring(target_source or ""),
                tonumber(combat_alive_ms) or 0,
                tonumber(task_monsters.count) or 0,
                has_special and "true" or "false",
                type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil",
                tostring(objective_mode),
                state.task_combat_force_kite == true and "true" or "false",
                type(kite_target) == "table" and tostring(kite_target.path_index or "") or "",
                type(kite_target) == "table" and tostring(kite_target.path_points or "") or ""
            ))
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end
        if generic_followup_ready
            and combat_alive_ms >= generic_followup_refresh_ms
            and (not generic_followup_require_no_special or not has_special)
        then
            clear_task_combat_state()
            log_throttled(ctx, "task_combat_generic_followup", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] generic combat lingered after objective, refresh next task | task=%s source=%s combat_ms=%d monsters=%d has_special=%s goal_distance=%s",
                tostring(task_name or state.current_task_name or ""),
                tostring(target_source or ""),
                tonumber(combat_alive_ms) or 0,
                tonumber(task_monsters.count) or 0,
                has_special and "true" or "false",
                type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil"
            ))
            schedule_task_refresh_after_transition(ctx, current_time, "task_combat_generic_followup", POST_DIALOGUE_SETTLE_MS)
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end
        return false
    end

    local last_seen_at = tonumber(state.task_combat_last_seen_at) or 0
    if last_seen_at <= 0 then
        return false
    end

    local idle_ms = current_time - last_seen_at
    local boss_clear_settle_ms = math.max(
        1500,
        tonumber(type(objective_cfg) == "table" and objective_cfg.boss_clear_settle_ms) or 8000
    )
    if boss_kite_objective and idle_ms < boss_clear_settle_ms then
        local kite_target = M.build_task_combat_kite_target(current_time, player_x, player_y, destination or target)
        state.stage = "task_combat_kite"
        if type(kite_target) == "table" then
            M.issue_task_combat_kite_move(ctx, current_time, kite_target)
        end
        issue_combat_pulse(ctx, current_time, "boss_target_lost_kite", true)
        log_throttled(ctx, "task_combat_boss_target_lost_sticky", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] boss target temporarily lost, continue fixed kite route | idle_ms=%d keep_ms=%d route_index=%s/%s goal_distance=%s objective_mode=%s task=%s",
            tonumber(idle_ms) or 0,
            tonumber(boss_clear_settle_ms) or 0,
            type(kite_target) == "table" and tostring(kite_target.path_index or "") or "",
            type(kite_target) == "table" and tostring(kite_target.path_points or "") or "",
            type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil",
            tostring(objective_mode),
            tostring(task_name or state.current_task_name or "")
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if idle_ms < TASK_COMBAT_CLEAR_SETTLE_MS then
        state.stage = "task_combat_complete_settle"
        hold_navigation(ctx, current_time, "task_combat_complete_settle")
        log_throttled(ctx, "task_combat_complete_settle", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] combat target disappeared, wait settle before objective follow-up | idle_ms=%d stage=%s last_count=%d monster_err=%s",
            tonumber(idle_ms) or 0,
            stage_name,
            tonumber(state.task_combat_last_count) or 0,
            tostring(monster_err or "")
        ))
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if M.maybe_handle_post_combat_loot(ctx, current_time, player_x, player_y, player_z, objective_cfg) then
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    clear_task_combat_state()
    clear_runtime_objective_caches()
    log_throttled(ctx, "task_combat_complete_followup", "info", LOG_THROTTLE_MS, string.format(
        "[Leveling] combat target cleared, resume objective handlers | idle_ms=%d goal_distance=%s objective_ready_distance=%s",
        tonumber(idle_ms) or 0,
        type(goal_distance) == "number" and string.format("%.2f", goal_distance) or "nil",
        type(objective_ready_distance) == "number" and string.format("%.2f", objective_ready_distance) or "nil"
    ))

    if M.maybe_handle_npc_dialogue_combat_retry(ctx, current_time, player_x, player_y, player_z) then
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    local followup_route_action_key = type(objective_cfg) == "table"
        and tostring(objective_cfg.followup_route_action_key or "")
        or ""
    if followup_route_action_key ~= "" then
        local action, action_err = M.activate_route_point_action(
            ctx,
            current_time,
            followup_route_action_key,
            "combat_followup"
        )
        if type(action) == "table" then
            log_throttled(ctx, "task_combat_followup_route_action", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] objective follow-up route action armed | task=%s objective=%s action=%s mode=%s",
                tostring(task_name or state.current_task_name or ""),
                tostring(type(objective_cfg) == "table" and objective_cfg.key or ""),
                tostring(followup_route_action_key),
                tostring(action.mode or "")
            ))
            if tostring(action.mode or "") == "npc_dialogue_point"
                and M.maybe_handle_route_point_action_npc_dialogue(ctx, current_time, player_x, player_y, player_z)
            then
                log_heartbeat(ctx, current_time, player_x, player_y, player_z)
                return true
            end
            if tostring(action.mode or "") == "lift_transition"
                and M.maybe_handle_route_point_action_boarding(ctx, current_time, player_x, player_y, player_z)
            then
                log_heartbeat(ctx, current_time, player_x, player_y, player_z)
                return true
            end
            if tostring(action.mode or "") == "recorded_route_point"
                and M.maybe_handle_route_point_action_route(ctx, current_time, player_x, player_y, player_z)
            then
                log_heartbeat(ctx, current_time, player_x, player_y, player_z)
                return true
            end
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end
        log_throttled(ctx, "task_combat_followup_route_action_failed", "warn", LOG_THROTTLE_MS, string.format(
            "[Leveling] objective follow-up route action unavailable | task=%s objective=%s action=%s err=%s",
            tostring(task_name or state.current_task_name or ""),
            tostring(type(objective_cfg) == "table" and objective_cfg.key or ""),
            tostring(followup_route_action_key),
            tostring(action_err or "")
        ))
    end

    if type(goal_distance) == "number"
        and type(objective_ready_distance) == "number"
        and goal_distance <= math.max(objective_ready_distance, TARGET_REACHED_DISTANCE)
    then
        return M.maybe_handle_task_reached(
            ctx,
            current_time,
            player_x,
            player_y,
            player_z,
            hp_ratio,
            target,
            destination,
            destination_distance,
            goal_distance
        )
    end

    schedule_task_refresh_after_transition(ctx, current_time, "task_combat_cleared", POST_DIALOGUE_SETTLE_MS)
    log_heartbeat(ctx, current_time, player_x, player_y, player_z)
    return true
end

function M.update(now, ctx)
    if state.running ~= true then
        return false, "Leveling runner is not started."
    end

    local current_time = tonumber(now) or now_ms(ctx)
    if current_time < (tonumber(state.next_tick_at) or 0) then
        return true
    end

    state.next_tick_at = current_time + UPDATE_INTERVAL_MS
    state.ticks = (tonumber(state.ticks) or 0) + 1
    sync_nav_worker_feedback(ctx, current_time)

    local nav_ok, nav_err = ensure_nav_ready(ctx, current_time)
    if not nav_ok then
        state.stage = "wait_nav"
        M.log_execution_trace(ctx, current_time, "nav_not_ready", nil, nil, nil, nil)
        release_async_combat_inputs(ctx, current_time, true)
        hold_navigation(ctx, current_time, "wait_nav")
        log_throttled(ctx, "nav_retry", "warn", LOG_THROTTLE_MS,
            "[Leveling] waiting nav init: " .. tostring(nav_err)
                .. " | last_call=" .. M.format_last_main_task_call_debug(current_time))
        log_heartbeat(ctx, current_time)
        return true
    end

    release_async_combat_inputs(ctx, current_time, false)

    if read_loading_state(ctx) then
        state.stage = "loading"
        M.log_execution_trace(ctx, current_time, "loading_state", nil, nil, nil, nil)
        state.next_task_refresh_at = 0
        M.arm_loading_transition_reacquire(ctx, current_time, "loading_state")
        release_async_combat_inputs(ctx, current_time, true)
        hold_navigation(ctx, current_time, "loading")
        log_throttled(ctx, "loading", "info", LOG_THROTTLE_MS,
            "[Leveling] game is loading, waiting.")
        log_heartbeat(ctx, current_time)
        return true
    end

    local in_main_interface, main_interface_err = read_main_interface_state(ctx)
    if in_main_interface == nil and main_interface_err then
        log_throttled(ctx, "main_interface_api_error", "warn", LOG_THROTTLE_MS,
            "[Leveling] is_main_interface failed: " .. tostring(main_interface_err))
    end

    local _, hp, max_hp, hp_ratio = refresh_player_status(ctx, current_time)
    local player_x, player_y, player_z, pos_err = read_player_pos(ctx)
    if player_x == nil or player_y == nil then
        release_async_combat_inputs(ctx, current_time, true)
        hold_navigation(ctx, current_time, "wait_pos")
        if maybe_handle_revive(ctx, current_time, hp, player_x, player_y, player_z, in_main_interface) then
            return true
        end
        if in_main_interface == false then
            state.stage = "wait_main_interface"
            M.log_execution_trace(ctx, current_time, "main_interface_false_wait", nil, nil, nil, nil)
            state.next_task_refresh_at = 0
            M.arm_loading_transition_reacquire(ctx, current_time, "main_interface_false_wait")
            log_throttled(ctx, "main_interface", "info", LOG_THROTTLE_MS,
                "[Leveling] IsMainInterface=false and player position unavailable, waiting.")
        else
            state.stage = "wait_pos"
            M.log_execution_trace(ctx, current_time, "player_pos_unavailable", nil, nil, nil, nil)
            log_throttled(ctx, "player_pos_failed", "warn", LOG_THROTTLE_MS,
                "[Leveling] player position unavailable: " .. tostring(pos_err)
                    .. " | nav=" .. M.nav_debug_state_text(ctx)
                    .. " | last_call=" .. M.format_last_main_task_call_debug(current_time))
        end
        log_heartbeat(ctx, current_time)
        return true
    end

    state.last_known_player_x = player_x
    state.last_known_player_y = player_y
    state.last_known_player_z = player_z

    if in_main_interface == false then
        log_throttled(ctx, "main_interface_false_but_pos_ok", "info", LOG_THROTTLE_MS,
            "[Leveling] IsMainInterface=false but player position is available, continue leveling.")
    end

    if maybe_handle_revive(ctx, current_time, hp, player_x, player_y, player_z, in_main_interface) then
        return true
    end

    if should_use_map_runtime_detection() then
        local current_map_name, _, map_info_err = refresh_current_map_info(ctx, current_time)
        if current_map_name == nil and map_info_err then
            log_throttled(ctx, "map_info_failed", "warn", LOG_THROTTLE_MS,
                "[Leveling] current map unavailable: " .. tostring(map_info_err))
        end
    end

    if M.maybe_handle_loading_transition_reacquire(ctx, current_time, player_x, player_y, player_z) then
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    update_progress_anchor(current_time, player_x, player_y)
    M.maybe_handle_potion_watch(ctx, current_time, hp, max_hp, hp_ratio)

    if M.maybe_handle_startup_state(ctx, current_time, player_x, player_y, player_z, hp_ratio) then
        return true
    end

    if M.maybe_handle_treasure_dungeon(ctx, current_time, player_x, player_y, player_z) then
        M.log_execution_trace(ctx, current_time, "treasure_dungeon", state.task_target, nil, nil, nil, player_x, player_y, player_z)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if state.revive_reentry_pending == true
        and maybe_handle_map_specific_transition(ctx, current_time, player_x, player_y)
    then
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if M.maybe_handle_route_point_action_boarding(ctx, current_time, player_x, player_y, player_z) then
        M.log_execution_trace(ctx, current_time, "route_point_action_boarding", state.task_target, nil, nil, nil, player_x, player_y, player_z)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if M.maybe_handle_route_point_action_npc_dialogue(ctx, current_time, player_x, player_y, player_z) then
        M.log_execution_trace(ctx, current_time, "route_point_action_npc_dialogue", state.task_target, nil, nil, nil, player_x, player_y, player_z)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if M.maybe_handle_route_point_action_route(ctx, current_time, player_x, player_y, player_z) then
        M.log_execution_trace(ctx, current_time, "route_point_action_recorded_route", state.task_target, nil, nil, nil, player_x, player_y, player_z)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if not M.is_task_combat_or_post_loot_active()
        and M.maybe_handle_task_dialogue_flow(ctx, current_time)
    then
        M.log_execution_trace(ctx, current_time, "task_dialogue_flow", state.task_target, nil, nil, nil, player_x, player_y, player_z)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if not M.is_task_combat_or_post_loot_active()
        and M.maybe_click_dialogue_jump_button(ctx, current_time)
    then
        state.stage = "dialogue_jump"
        M.log_execution_trace(ctx, current_time, "dialogue_jump_button", state.task_target, nil, nil, nil, player_x, player_y, player_z)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if not M.is_task_combat_or_post_loot_active()
        and M.maybe_handle_post_dialogue_flow(ctx, current_time)
    then
        M.log_execution_trace(ctx, current_time, "post_dialogue_flow", state.task_target, nil, nil, nil, player_x, player_y, player_z)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if current_time < (tonumber(state.task_update_wait_until) or 0) then
        state.stage = "wait_task_update"
        M.log_execution_trace(ctx, current_time, "waiting_task_update", nil, nil, nil, nil, player_x, player_y, player_z)
        release_async_combat_inputs(ctx, current_time, true)
        hold_navigation(ctx, current_time, "wait_task_update")
        log_throttled(ctx, "task_update_wait", "info", LOG_THROTTLE_MS,
            "[Leveling] waiting task list update after dialogue/prompt settle.")
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if state.require_task_button_refresh == true then
        state.stage = "refresh_task_button_after_dialogue"
        M.log_execution_trace(ctx, current_time, "require_task_button_refresh", nil, nil, nil, nil, player_x, player_y, player_z)
        release_async_combat_inputs(ctx, current_time, true)
        hold_navigation(ctx, current_time, "refresh_task_button_after_dialogue")
        if current_time >= (tonumber(state.next_task_button_click_at) or 0) then
            local clicked = click_main_task_button(ctx, current_time)
            if clicked then
                state.require_task_button_refresh = false
                state.require_task_button_refresh_reason = nil
            else
                log_throttled(ctx, "refresh_task_button_wait", "info", LOG_THROTTLE_MS,
                    "[Leveling] waiting to reacquire refreshed main task button after dialogue.")
            end
        end
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    local task_entry_action_active = M.is_task_entry_action_active(current_time)

    if type(state.task_target) ~= "table"
        and not ((tonumber(state.post_revive_boss_engage_until) or 0) > current_time)
        and not ((tonumber(state.startup_state_resolve_until) or 0) > current_time)
        and not M.should_suspend_treasure_task_refresh()
        and task_entry_action_active ~= true
        and current_time >= (tonumber(state.next_task_button_click_at) or 0)
    then
        state.stage = "click_task_button"
        M.log_execution_trace(ctx, current_time, "click_task_button", nil, nil, nil, nil, player_x, player_y, player_z)
        click_main_task_button(ctx, current_time)
    elseif type(state.task_target) ~= "table" and task_entry_action_active == true then
        log_throttled(ctx, "task_button_click_suppressed_by_task_entry_action", "info", LOG_THROTTLE_MS, string.format(
            "[Leveling] task entry action suppresses main task reacquire click | task=%s stage=%s",
            tostring(M.current_task_log_detail() or state.current_task_name or ""),
            tostring(state.stage or "")
        ))
    end

    local target_synced_this_tick = false
    local waiting_for_task_path = (type(state.task_target) ~= "table"
        and tonumber(state.last_task_button_click_at) ~= nil
        and state.last_task_button_click_at > 0
        and current_time < math.max(
            state.last_task_button_click_at + TASK_BUTTON_SETTLE_MS,
            tonumber(state.task_path_wait_until) or 0
        ))
        or task_entry_action_active == true

    if waiting_for_task_path and current_time >= (tonumber(state.next_task_refresh_at) or 0) then
        update_task_target(ctx, current_time, player_x, player_y)
        target_synced_this_tick = true
    end

    if waiting_for_task_path and type(state.task_target) ~= "table" then
        if M.maybe_handle_task_entry_action(ctx, current_time, player_x, player_y, player_z) then
            return true
        end
        state.stage = "wait_task_path_after_button"
        M.log_execution_trace(ctx, current_time, "waiting_task_path_after_button", nil, nil, nil, nil, player_x, player_y, player_z)
        release_async_combat_inputs(ctx, current_time, true)
        hold_navigation(ctx, current_time, "wait_task_path_after_button")
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if current_time >= (tonumber(state.next_task_refresh_at) or 0) or type(state.task_target) ~= "table" then
        update_task_target(ctx, current_time, player_x, player_y)
        target_synced_this_tick = true
    end

    if type(state.task_path) == "table"
        and #state.task_path > 0
        and tonumber(state.last_task_path_sync_at) ~= current_time
        and target_synced_this_tick ~= true
    then
        local live_target = select(1, sync_task_path_target(ctx, player_x, player_y, current_time))
        state.last_task_path_sync_at = current_time
        if type(live_target) == "table" then
            assign_task_target(ctx, current_time, live_target)
        end
    end

    local target = state.task_target
    if type(target) ~= "table" then
        if current_time >= (tonumber(state.next_task_name_probe_at) or 0) then
            local hint_x, hint_y = resolve_main_task_button_hint(ctx)
            if hint_x ~= nil and hint_y ~= nil then
                M.refresh_current_task_name(ctx, current_time, nil, hint_x, hint_y)
            end
            state.next_task_name_probe_at = current_time + 800
        end
        if (tonumber(state.post_revive_boss_engage_until) or 0) > current_time then
            local revive_destination = nil
            local revive_goal_distance = 0
            if is_valid_world_point(state.task_pos) then
                revive_destination = {
                    x = tonumber(state.task_pos.x),
                    y = tonumber(state.task_pos.y),
                    z = tonumber(state.task_pos.z),
                    source = "post_revive_task_pos"
                }
                revive_goal_distance = distance_2d(player_x, player_y, revive_destination.x, revive_destination.y)
            end
            if M.maybe_handle_forced_kite_monster(
                ctx,
                current_time,
                player_x,
                player_y,
                player_z,
                hp_ratio,
                revive_destination,
                revive_destination,
                revive_goal_distance
            ) then
                return true
            end
        end
        if (tonumber(state.startup_boss_engage_until) or 0) > current_time then
            if M.maybe_handle_forced_kite_monster(
                ctx,
                current_time,
                player_x,
                player_y,
                player_z,
                hp_ratio,
                nil,
                nil,
                nil
            ) then
                return true
            end
        end
        if maybe_handle_map_specific_transition(ctx, current_time, player_x, player_y) then
            log_heartbeat(ctx, current_time, player_x, player_y, player_z)
            return true
        end
        if M.maybe_handle_low_priority_task_ui(ctx, current_time, player_x, player_y, player_z, {
            phase = "wait_task",
            has_target = false
        }) then
            return true
        end
        state.stage = "wait_task"
        M.log_execution_trace(ctx, current_time, "no_task_target_wait", nil, nil, nil, nil, player_x, player_y, player_z)
        release_async_combat_inputs(ctx, current_time, true)
        hold_navigation(ctx, current_time, "wait_task")
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    local target_distance = distance_2d(player_x, player_y, target.x, target.y)
    local destination = build_task_destination_point(player_x, player_y)
    local destination_distance = destination
        and distance_2d(player_x, player_y, destination.x, destination.y)
        or nil
    local goal_distance = destination_distance or target_distance
    local map_cfg = select(1, current_map_task_config())
    local task_cfg = select(1, M.current_task_runtime_config())
    local point_objective_cfg = M.current_objective_point_config(destination, target)
    local objective_cfg = type(task_cfg) == "table" and type(task_cfg.objective) == "table" and task_cfg.objective
        or (type(map_cfg) == "table" and type(map_cfg.objective) == "table" and map_cfg.objective or nil)
        or point_objective_cfg
    local objective_ready_distance = type(M._leveling_policy) == "table"
        and type(M._leveling_policy.objective_ready_distance) == "function"
        and M._leveling_policy.objective_ready_distance(TARGET_REACHED_DISTANCE, objective_cfg)
        or TARGET_REACHED_DISTANCE

    if M.maybe_handle_route_point_action(ctx, current_time, player_x, player_y, player_z) then
        M.log_execution_trace(ctx, current_time, "route_point_action", target, destination, goal_distance, objective_cfg and objective_cfg.mode, player_x, player_y, player_z)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if maybe_handle_map_specific_transition(ctx, current_time, player_x, player_y) then
        M.log_execution_trace(ctx, current_time, "map_specific_transition", target, destination, goal_distance, objective_cfg and objective_cfg.mode, player_x, player_y, player_z)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    -- Task PortalBtn is part of the main quest progression chain. It must be
    -- visible-driven and low-frequency, but it also must not wait for a stall
    -- or task_reached gate, otherwise the runner can walk past the door and
    -- keep following the stale route.
    if not M.is_task_combat_or_post_loot_active()
        and M.maybe_click_global_task_portal(ctx, current_time, {
            phase = "follow_task",
            player_x = player_x,
            player_y = player_y,
            target = target,
            destination = destination,
            goal_distance = goal_distance
        })
    then
        state.stage = "global_task_portal"
        M.log_execution_trace(ctx, current_time, "global_task_portal_clicked", target, destination, goal_distance, objective_cfg and objective_cfg.mode, player_x, player_y, player_z)
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if M.maybe_handle_task_combat_completion(
        ctx,
        current_time,
        player_x,
        player_y,
        player_z,
        hp_ratio,
        target,
        destination,
        destination_distance,
        goal_distance,
        objective_ready_distance
    ) then
        return true
    end

    if M.maybe_handle_forced_kite_monster(
        ctx,
        current_time,
        player_x,
        player_y,
        player_z,
        hp_ratio,
        target,
        destination,
        goal_distance
    ) then
        return true
    end

    if goal_distance > objective_ready_distance then
        state.task_reached_unresolved_since = 0
    end

    if goal_distance <= objective_ready_distance then
        M.log_execution_trace(ctx, current_time, "goal_distance_ready_task_reached", target, destination, goal_distance, objective_cfg and objective_cfg.mode, player_x, player_y, player_z)
        return M.maybe_handle_task_reached(
            ctx,
            current_time,
            player_x,
            player_y,
            player_z,
            hp_ratio,
            target,
            destination,
            destination_distance,
            goal_distance
        )
    end

    local last_move_call_at = tonumber(state.last_move_call_at) or 0
    local is_stalled = last_move_call_at > 0
        and current_time - last_move_call_at >= STUCK_MOVE_GRACE_MS
        and current_time - (tonumber(state.last_progress_at) or 0) >= STUCK_RETRY_INTERVAL_MS
    if is_stalled then
        state.stall_retry_count = (tonumber(state.stall_retry_count) or 0) + 1
        state.next_move_at = 0
        state.last_progress_at = current_time
        if (tonumber(state.stall_retry_count) or 0) >= 2 then
            state.next_task_refresh_at = 0
            state.next_follow_task_button_refresh_at = math.min(
                tonumber(state.next_follow_task_button_refresh_at) or current_time,
                current_time
            )
            state.next_task_button_click_at = math.min(
                tonumber(state.next_task_button_click_at) or current_time,
                current_time
            )
            log_throttled(ctx, "stuck_retry", "warn", LOG_THROTTLE_MS, string.format(
                "[Leveling] progress looks stalled, refresh task button and MoveTo. stall_retry_count=%d",
                tonumber(state.stall_retry_count) or 0
            ))
        else
            log_throttled(ctx, "stuck_retry_move_only", "info", LOG_THROTTLE_MS, string.format(
                "[Leveling] progress looks stalled, retry MoveTo without task button refresh. stall_retry_count=%d",
                tonumber(state.stall_retry_count) or 0
            ))
        end
    end

    if is_stalled and M.maybe_handle_low_priority_task_ui(ctx, current_time, player_x, player_y, player_z, {
        phase = "follow_task",
        has_target = true,
        is_stalled = true,
        goal_distance = goal_distance,
        objective_ready_distance = objective_ready_distance
    }) then
        return true
    end

    if is_stalled and (tonumber(state.stall_retry_count) or 0) >= 2
        and maybe_refresh_task_button_during_follow(ctx, current_time, goal_distance, true)
    then
        state.stage = "refresh_task_button_follow"
        M.log_execution_trace(ctx, current_time, "stall_refresh_follow", target, destination, goal_distance, objective_cfg and objective_cfg.mode, player_x, player_y, player_z)
        hold_navigation(ctx, current_time, "refresh_task_button_follow")
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if maybe_refresh_task_button_for_route_deviation(ctx, current_time, player_x, player_y, target) then
        state.stage = "refresh_task_button_route_deviation"
        M.log_execution_trace(ctx, current_time, "route_deviation_refresh", target, destination, goal_distance, objective_cfg and objective_cfg.mode, player_x, player_y, player_z)
        hold_navigation(ctx, current_time, "refresh_task_button_route_deviation")
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if maybe_refresh_task_button_for_path_loss(ctx, current_time, target, destination_distance) then
        state.stage = "refresh_task_button_path_loss"
        M.log_execution_trace(ctx, current_time, "path_loss_refresh", target, destination, goal_distance, objective_cfg and objective_cfg.mode, player_x, player_y, player_z)
        hold_navigation(ctx, current_time, "refresh_task_button_path_loss")
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    if M.maybe_refresh_task_button_for_follow_idle(ctx, current_time, target, destination_distance, goal_distance) then
        state.stage = "refresh_task_button_follow_idle"
        M.log_execution_trace(ctx, current_time, "follow_idle_refresh", target, destination, goal_distance, objective_cfg and objective_cfg.mode, player_x, player_y, player_z)
        hold_navigation(ctx, current_time, "refresh_task_button_follow_idle")
        log_heartbeat(ctx, current_time, player_x, player_y, player_z)
        return true
    end

    state.stage = "follow_task"
    M.log_execution_trace(ctx, current_time, "follow_task_move", target, destination, goal_distance, objective_cfg and objective_cfg.mode, player_x, player_y, player_z)
    issue_move(ctx, current_time, target)
    maybe_soft_refresh_task_button_during_follow(ctx, current_time, goal_distance)

    local should_pulse, pulse_reason = false, nil
    local nearby_monsters = nil
    local monster_err = nil
    nearby_monsters, monster_err = M.find_task_monsters(ctx, current_time, player_x, player_y)
    if nearby_monsters and tonumber(nearby_monsters.count) and nearby_monsters.count > 0 then
        local nearest_monster = nearby_monsters.nearest or {}
        log_throttled(ctx, "nearby_monster_attack", "info", MONSTER_SCAN_LOG_INTERVAL_MS, string.format(
            "[Leveling] nearby monsters detected | count=%d total=%d nearest=%s player_distance=%.2f",
            tonumber(nearby_monsters.count) or 0,
            tonumber(nearby_monsters.total_count) or tonumber(nearby_monsters.count) or 0,
            tostring(nearest_monster.label or ""),
            tonumber(nearest_monster.distance) or 0
        ))
        if M.should_suppress_follow_nearby_monster_pulse(task_cfg, objective_cfg, target, goal_distance, objective_ready_distance) then
            log_throttled(ctx, "nearby_monster_pulse_suppressed", "info", MONSTER_SCAN_LOG_INTERVAL_MS, string.format(
                "[Leveling] nearby monster pulse suppressed during boss approach | nearest_distance=%.2f count=%d goal_distance=%.2f threshold=%.2f",
                tonumber(nearest_monster.distance) or 0,
                tonumber(nearby_monsters.count) or 0,
                tonumber(goal_distance) or 0,
                tonumber(type(task_cfg) == "table" and task_cfg.approach_suppress_nearby_monster_pulse_goal_distance)
                    or tonumber(type(objective_cfg) == "table" and objective_cfg.approach_suppress_nearby_monster_pulse_goal_distance)
                    or 0
            ))
        else
            should_pulse, pulse_reason = should_issue_nearby_monster_pulse(current_time)
            if should_pulse then
                local pulse_ok, pulse_err = issue_combat_pulse(ctx, current_time, "nearby_monster", true)
                if not pulse_ok then
                    log_throttled(ctx, "nearby_monster_pulse_blocked", "info", MONSTER_SCAN_LOG_INTERVAL_MS, string.format(
                        "[Leveling] nearby monster pulse blocked | nearest_distance=%.2f count=%d reason=%s",
                        tonumber(nearest_monster.distance) or 0,
                        tonumber(nearby_monsters.count) or 0,
                        tostring(pulse_err or "")
                    ))
                end
                should_pulse = false
            else
                log_throttled(ctx, "nearby_monster_pulse_deferred", "info", MONSTER_SCAN_LOG_INTERVAL_MS, string.format(
                    "[Leveling] nearby monster pulse deferred | nearest_distance=%.2f count=%d reason=%s",
                    tonumber(nearest_monster.distance) or 0,
                    tonumber(nearby_monsters.count) or 0,
                    tostring(pulse_reason or "")
                ))
            end
        end
    elseif monster_err then
        log_throttled(ctx, "nearby_monster_scan_miss", "info", MONSTER_SCAN_LOG_INTERVAL_MS,
            "[Leveling] monster scan found no nearby target: " .. tostring(monster_err))
    end

    if not should_pulse then
        should_pulse, pulse_reason = should_issue_follow_move_pulse(current_time, goal_distance, is_stalled, target)
    end
    if not should_pulse then
        should_pulse, pulse_reason = should_issue_combat_pulse(current_time, goal_distance, is_stalled)
    end
    if should_pulse then
        issue_combat_pulse(ctx, current_time, pulse_reason)
    end
    log_heartbeat(ctx, current_time, player_x, player_y, player_z)
    return true
end

function M.stop(ctx)
    release_async_combat_inputs(ctx, now_ms(ctx), true)
    stop_nav_worker(ctx)
    if state.running == true then
        logger(ctx).info("[Leveling] runner stopped")
    end
    if type(M._leveling_treasure) == "table"
        and type(M._leveling_treasure.save_resume_snapshot) == "function"
    then
        M._leveling_treasure.save_resume_snapshot(ctx, state, "leveling_stop")
    end
    reset_state()
end

return M


