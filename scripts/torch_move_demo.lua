local function load_nav_module()
    local ok, mod = pcall(require, "torch_nav")
    if ok then
        return mod
    end

    ok, mod = pcall(require, "scripts.torch_nav")
    if ok then
        return mod
    end

    local chunk = loadfile_with_bytecode_fallback("scripts/torch_nav.lua", "torch_nav")
    return chunk()
end

function loadfile_with_bytecode_fallback(path, label)
    local candidates = { path }
    if type(path) == "string" and path ~= "" then
        if path:sub(-4):lower() == ".lua" then
            candidates[#candidates + 1] = path:sub(1, -5) .. ".luac"
        elseif path:sub(-5):lower() ~= ".luac" then
            candidates[#candidates + 1] = path .. ".luac"
        end
    end

    local last_err = nil
    for _, candidate in ipairs(candidates) do
        local chunk, err = loadfile(candidate)
        if chunk then
            return chunk
        end
        last_err = err
    end

    error(string.format("load %s failed: %s", tostring(label or path), tostring(last_err)))
end

local nav = load_nav_module()

local function load_human_mouse_module()
    local ok, mod = pcall(require, "human_mouse")
    if ok then
        return mod
    end

    ok, mod = pcall(require, "scripts.human_mouse")
    if ok then
        return mod
    end

    local chunk = loadfile_with_bytecode_fallback("scripts/human_mouse.lua", "human_mouse")
    return chunk()
end

local human_mouse = load_human_mouse_module()

local function script_file_path()
    local info = debug.getinfo(1, "S")
    local source = info and info.source or ""
    if source:sub(1, 1) == "@" then
        return source:sub(2)
    end
    return nil
end

local SCRIPT_FILE = script_file_path() or "scripts/torch_move_demo.lua"
local SCRIPT_DIR = SCRIPT_FILE:match("^(.*)[/\\][^/\\]+$") or "."
local PROJECT_ROOT = SCRIPT_DIR:match("^(.*)[/\\]scripts$") or "."
local CURRENT_WORK_DIR = type(sys) == "table" and type(sys.get_cwd) == "function" and sys.get_cwd() or "."

local function resolve_project_path(path)
    if type(path) ~= "string" or path == "" then
        return path
    end

    if path:match("^%a:[/\\]") or path:match("^[/\\]") then
        return path
    end

    local separator = package.config:sub(1, 1)
    local normalized = path:gsub("[/\\]", separator)
    local base_dir = PROJECT_ROOT
    if base_dir == "." or base_dir == "" then
        base_dir = CURRENT_WORK_DIR
    end
    if base_dir == "." or base_dir == "" then
        return normalized
    end

    return base_dir .. separator .. normalized
end

local function free_image(img)
    if img and type(vision) == "table" and type(vision.free) == "function" then
        vision.free(img)
    end
end

local function save_f2_debug_capture(img, suffix)
    if not img or type(vision) ~= "table" or type(vision.save) ~= "function" then
        return nil
    end

    local name = "logs/f2_capture_" .. tostring(suffix or "latest") .. ".png"
    local path = resolve_project_path(name)
    local ok = vision.save(img, path)
    if not ok then
        return nil
    end

    return path
end

local function valid_image(img)
    if not img then
        return false
    end

    if type(img.valid) == "function" then
        return img:valid()
    end

    return true
end

local function trim(text)
    if not text then
        return ""
    end
    return (tostring(text):gsub("^%s+", ""):gsub("%s+$", ""))
end

local function portal_number(value)
    if type(value) == "number" then
        return value
    end
    if type(value) == "string" then
        return tonumber(value)
    end
    return nil
end

local function portal_pick_number(tbl, keys)
    if type(tbl) ~= "table" then
        return nil
    end

    for _, key in ipairs(keys) do
        local value = portal_number(tbl[key])
        if value ~= nil then
            return value
        end
    end

    return nil
end

local function portal_extract_position(info)
    if type(info) ~= "table" then
        return nil, nil, nil
    end

    local x = portal_pick_number(info, { "x", "X", "posX", "PosX", "worldX", "WorldX" })
    local y = portal_pick_number(info, { "y", "Y", "posY", "PosY", "worldY", "WorldY" })
    local z = portal_pick_number(info, { "z", "Z", "posZ", "PosZ", "worldZ", "WorldZ" })
    if x ~= nil and y ~= nil then
        return x, y, z
    end

    for _, key in ipairs({ "pos", "position", "coord", "coords", "point" }) do
        local nested = info[key]
        if type(nested) == "table" then
            x = portal_pick_number(nested, { "x", "X", "posX", "PosX", "worldX", "WorldX" })
            y = portal_pick_number(nested, { "y", "Y", "posY", "PosY", "worldY", "WorldY" })
            z = portal_pick_number(nested, { "z", "Z", "posZ", "PosZ", "worldZ", "WorldZ" })
            if x ~= nil and y ~= nil then
                return x, y, z
            end
        end
    end

    return nil, nil, nil
end

local function portal_is_scalar(value)
    local value_type = type(value)
    return value_type == "string"
        or value_type == "number"
        or value_type == "boolean"
end

local function portal_format_scalar(value)
    if type(value) == "string" then
        local text = value:gsub("[\r\n\t]", " ")
        if #text > 120 then
            text = text:sub(1, 117) .. "..."
        end
        return text
    end
    return tostring(value)
end

local function portal_shallow_summary(tbl, max_items)
    if type(tbl) ~= "table" then
        return tostring(tbl)
    end

    local keys = {}
    for key, value in pairs(tbl) do
        if (type(key) == "string" or type(key) == "number") and portal_is_scalar(value) then
            keys[#keys + 1] = key
        end
    end

    table.sort(keys, function(a, b)
        return tostring(a) < tostring(b)
    end)

    local parts = {}
    local limit = math.max(1, tonumber(max_items) or 16)
    for index, key in ipairs(keys) do
        if index > limit then
            parts[#parts + 1] = "..."
            break
        end
        parts[#parts + 1] = tostring(key) .. "=" .. portal_format_scalar(tbl[key])
    end

    if #parts == 0 then
        return "{}"
    end

    return table.concat(parts, " ")
end

local function dump_nearby_portals()
    local items, err = nav.enum_portals()
    if items == nil then
        return false, err or "EnumPortal failed."
    end

    log.info(string.format("Nearby portals enumerated | count=%d", #items))
    for index, item in ipairs(items) do
        local x, y, z = portal_extract_position(item)
        local position = ""
        if x ~= nil and y ~= nil then
            position = string.format(" pos=(%.2f, %.2f, %.2f)", x, y, z or 0)
        end
        log.info(string.format(
            "Portal %d/%d | %s%s",
            index,
            #items,
            portal_shallow_summary(item, 20),
            position
        ))
    end

    return true
end

local PROCESS_NAME = "torchlight_infinite.exe"
local MODE = "driver"

local ARRIVE_TOLERANCE = 120
local REPATH_INTERVAL_MS = 1500
local POLL_INTERVAL_MS = 30
local INIT_RETRY_MS = 3000
local ROUTE_PROGRESS_DISTANCE = 80
local CUSTOM_ROUTE_STUCK_TIMEOUT_MS = 4500
local CUSTOM_ROUTE_KICKSTART_ENABLED = false
local CUSTOM_ROUTE_KICK_CLICK_RADIUS = 140
local CUSTOM_ROUTE_KICK_DELAY_MS = 160
local MOVE_CALL_MOUSE_SYNC_ENABLED = false
local MOVE_CALL_MOUSE_SYNC_MODE = "direction"
local MOVE_CALL_MOUSE_SYNC_RADIUS = 140
local MOVE_CALL_MOUSE_SYNC_SWAP_AXES = false
local MOVE_CALL_MOUSE_SYNC_INVERT_X = true
local MOVE_CALL_MOUSE_SYNC_INVERT_Y = true
local MOVE_CALL_MOUSE_SYNC_SKIP_SAME_TARGET_UNTIL_NEAR = true
local MOVE_CALL_MOUSE_SYNC_NEAR_TARGET_DISTANCE = ARRIVE_TOLERANCE + 40

local ROUTE_POINTS = {
    { x = 1506.00, y = -2421.00, z = 3136.09 },
    { x = 1773.00, y = 831.00, z = 3092.00 },
    { x = 4175.00, y = 2885.00, z = 3116.41 },
    { x = 4945.27, y = 3439.51, z = 3296.01 },
    { x = 5649.00, y = 4562.00, z = 3604.81 }
}

local CUSTOM_ROUTE_POINTS = {
        {  x =336.00,  y =2064.00 },
    { x = 1584.00, y = 976.00 },
    {  x =639.00,  y =2616.00 },
    { x = 814.00, y = 900.00 },
    { x = -910.00, y = 705.00 },
    { x = -849.75, y = -1042.72 },
    { x = -769.00, y = 810.00 },
    { x = 2419.00, y = 776.00 },
    { x = 4066.00, y = 767.00 },
    { x = 3974.00, y = 3420.00 },
    { x = 4989.00, y = 4095.00 },
    { x = 5836.00, y = 3158.00 },
    { x = 5990.00, y = 2364.00 },
    { x = 5851.00, y = 795.00 },
    { x = 7286.00, y = -638.00 },
    { x = 7062.79, y = -2449.27 },
    { x = 6002.00, y = -2543.00 },
    { x = 2389.00, y = -2203.00 },
    { x = 3691.00, y = -2774.00 },
    { x = 3621.00, y = -5587.00 },
    { x = 2356.00, y = -5570.00 },
    { x = 4333.00, y = -5843.00 },
    { x = 4052.00, y = -8612.00 },
    { x = 5752.00, y = -8781.00 },
    { x = 5524.26, y = -5775.29 },
    { x = 8172.00, y = -5731.00 },
    { x = 10136.00, y = -5603.00 },
    { x = 9916.08, y = -1399.85 },
    { x = 11369.76, y = -785.01 },
    { x = 16875.26, y = -837.10 },
    { x = 20061.60, y = -855.53 },
    { x = 17917.00, y = -902.00 },
    { x = 18005.00, y = -3798.00 },
    { x = 18600.78, y = -4582.82 },
    { x = 18427.06, y = -7278.81 },
    { x = 15125.00, y = -7188.00 },
    { x = 14730.00, y = -3959.00 },
    { x = 16915.78, y = -4684.40 },
    { x = 16745.11, y = -10222.42 },
    { x = 16760.00, y = -12082.00 }
}

local HOTKEY_CUSTOM_ROUTE = 0x74
local HOTKEY_PICK_CONTROL = 0x75
local HOTKEY_MOVE = 0x2D
local HOTKEY_DUMP_CURRENT_SELECTED_API_F1 = 0x70
local HOTKEY_CLICK_ANNIVERSARY_REWARD = 0x71
local HOTKEY_DUMP_SELECTED_BUTTON = 0x72
local HOTKEY_DUMP_CURRENT_SELECTED_API = 0x73
local HOTKEY_PRINT_POS = 0x76
local HOTKEY_DUMP_VISIBLE_CONTROLS = 0x77
local HOTKEY_CLICK_BOTTOM_LEFT = 0x78
local HOTKEY_DUMP_BOTTOM_LEFT = 0x79
local HOTKEY_CLICK_COST_ICON = 0x7A
local HOTKEY_DUMP_PAGE_TEXT_CONTROLS = 0x7B
local HOTKEY_EXIT_CTRL = 0x11
local HOTKEY_EXIT = HOTKEY_DUMP_PAGE_TEXT_CONTROLS
local F3_TARGET_TEXT = "\u{4E9A}\u{4EBA}\u{6751}\u{843D}"
local F3_BUTTON_NAME = "UIButton Transient.GameEngine.CoreGameInstance.UIMysticMapItem_C.WidgetTree.ClickButton"
local F3_DISTANCE_MIN = 141.7
local F3_DISTANCE_MAX = 141.8
local F3_CURSOR_MAX_DISTANCE = 140
local F3_CURSOR_LIMIT = 50
local F4_TARGET_PRESET = {
    label = "Holy Court Maze ClickButton",
    anchor_exact_text = "\u{5723}\u{5EAD}\u{8FF7}\u{5BAB}",
    button_name = "UIButton Transient.GameEngine.CoreGameInstance.UIMysticMapItem_C.WidgetTree.ClickButton",
    distance_target = 114.453248,
    distance_tolerance = 0.2,
    distance_round_digits = 1
}
local HARD_CLICK_PATTERNS = {
    "mystery_c.widgettree.hardclickbtn",
    "hardclickbtn"
}
local F11_TARGET_PATTERNS = {
    "uimysticareaitem_c.widgettree.heidi_l01",
    "heidi_l01"
}
local F2_TARGET_PRESET = {
    label = "回收",
    template_path = "Ha/huishou.bmp",
    template_threshold = 0.99,
    click_button = "left",
    click_delay = 50,
    click_mode = "api",
    hover_delay_ms = 80,
    capture_set_foreground = true,
    capture_foreground_delay_ms = 60,
    save_capture_on_success = false,
    save_capture_on_fail = true,
    click_center_x = true,
    click_center_y = true,
    perform_click = false,
    click_offset_x = 0,
    click_offset_y = 0,
    include_patterns = {
        "mysteryarea_c.widgettree.commoncardbtn",
        "commoncardbtn",
        "开启传送门"
    },
    exclude_patterns = {
        "mysterybossdetail_c.widgettree.enterbtn",
        "mysterymapdetail_c.widgettree.openbtn"
    },
    related_texts = {
        "开启传送门"
    },
    related_text_max_distance = 80
}
local F12_TARGET_PRESETS = {
    deposit = {
        label = "DepositBtn",
        exact_texts = {
            "一键存入"
        },
        include_patterns = {
            "warehouse_c.widgettree.depositbtn",
            "depositbtn",
            "一键存入"
        }
    },
    activity_tag = {
        label = "ActivityTag UIButton",
        include_patterns = {
            "activitytagitem_c.widgettree.uibutton",
            "activitytagitem_c"
        },
        preferred_x = 16.895998001099,
        preferred_y = 166.65599060059,
        preferred_max_distance = 80
    },
    activity_tab = {
        label = "ActivityTab UIButton",
        include_patterns = {
            "activitytabitem_c.widgettree.uibutton",
            "activitytabitem_c"
        },
        preferred_x = 97.512542724609,
        preferred_y = 262.97073364258,
        preferred_max_distance = 80
    },
    activity_tab_sample = {
        label = "ActivityTab UIButton Sample",
        include_patterns = {
            "activitytabitem_c.widgettree.uibutton",
            "activitytabitem_c"
        },
        anchor_exact_texts = {
            "样本提取"
        },
        anchor_include_patterns = {
            "样本提取"
        },
        neighbor_include_patterns = {
            "activitytabitem_c.widgettree.uibutton",
            "activitytabitem_c"
        },
        neighbor_max_distance = 80,
        related_texts = {
            "样本提取"
        },
        related_text_max_distance = 80,
        preferred_x = 88.209030151367,
        preferred_y = 286.33892822266,
        preferred_max_distance = 80
    },
    rehab_guide = {
        label = "渴瘾症康复指南",
        include_patterns = {
            "activitytabitem_c.widgettree.uibutton",
            "activitytabitem_c"
        },
        anchor_exact_texts = {
            "渴瘾症康复指南"
        },
        anchor_include_patterns = {
            "渴瘾症康复指南"
        },
        neighbor_include_patterns = {
            "activitytabitem_c.widgettree.uibutton",
            "activitytabitem_c"
        },
        neighbor_max_distance = 100,
        related_texts = {
            "渴瘾症康复指南"
        },
        related_text_max_distance = 100
    },
    anniversary_reward = {
        label = "ActivityAnniversaryReward Button_Click",
        include_patterns = {
            "activityanniversaryrewarditem_c.widgettree.rewardgoodbaseicon.widgettree.goodbaseicon.widgettree.button_click",
            "activityanniversaryrewarditem_c",
            "button_click"
        },
        preferred_x = 299.44268798828,
        preferred_y = 574.11883544922,
        preferred_max_distance = 100
    },
    home_free_entry = {
        label = "HomeFree EntryButton",
        include_patterns = {
            "homefreebtnitem_c.widgettree.entrybutton",
            "homefreebtnitem_c",
            "entrybutton"
        },
        preferred_x = 1020.6790771484,
        preferred_y = 49.280284881592,
        preferred_max_distance = 100
    }
}
local AUTO_HANGUP_TARGETS = {
    mystic_area_frozen_abyss = {
        label = "冰封寒渊",
        anchor_exact_texts = {
            "冰封寒渊"
        },
        anchor_include_patterns = {
            "冰封寒渊"
        },
        neighbor_include_patterns = {
            "uimysticareaitem_c.widgettree.clickbutton",
            "uimysticareaitem_c",
            "clickbutton"
        },
        neighbor_max_distance = 120,
        preferred_x = 264.88598632812,
        preferred_y = 399.43914794922,
        preferred_max_distance = 120
    },
    mystic_map_watcher = {
        label = "冰封寒渊监视者",
        anchor_exact_texts = {
            "冰封寒渊监视者"
        },
        anchor_include_patterns = {
            "冰封寒渊监视者"
        },
        neighbor_include_patterns = {
            "uimysticmapitem_c.widgettree.clickbutton",
            "uimysticmapitem_c",
            "clickbutton"
        },
        neighbor_max_distance = 120,
        preferred_x = 365.50170898438,
        preferred_y = 315.00405883789,
        preferred_max_distance = 120
    },
    boss_detail_open_portal = {
        label = "开启传送门(BossDetail)",
        include_patterns = {
            "mysterybossdetail_c.widgettree.addcosticonbtn",
            "addcosticonbtn"
        },
        related_texts = {
            "开启传送门"
        },
        related_text_max_distance = 80,
        preferred_x = 1052.6646728516,
        preferred_y = 717.40588378906,
        preferred_max_distance = 100
    },
    skip_sequence = {
        label = "跳过",
        include_patterns = {
            "skiplevelsequence_c.widgettree.skipbutton",
            "skipbutton",
            "跳过"
        },
        preferred_x = 1057.4625244141,
        preferred_y = 68.208251953125,
        preferred_max_distance = 80
    },
    mystic_map_junk_block = {
        label = "杂物街区",
        anchor_exact_texts = {
            "杂物街区"
        },
        anchor_include_patterns = {
            "杂物街区"
        },
        neighbor_include_patterns = {
            "uimysticmapitem_c.widgettree.clickbutton",
            "uimysticmapitem_c",
            "clickbutton"
        },
        neighbor_max_distance = 120,
        preferred_x = 355.91790771484,
        preferred_y = 439.73962402344,
        preferred_max_distance = 120
    },
    map_detail_next_step = {
        label = "下一步",
        include_patterns = {
            "mysterymapdetail_c.widgettree.openbtn",
            "openbtn",
            "下一步"
        },
        preferred_x = 1009.7557983398,
        preferred_y = 745.33654785156,
        preferred_max_distance = 100
    },
    map_detail_back = {
        label = "返回上一步",
        include_patterns = {
            "mysterymapdetail_c.widgettree.uititlev2.widgettree.btnback",
            "btnback"
        },
        preferred_x = 1118.5920410156,
        preferred_y = 46.0,
        preferred_max_distance = 80
    },
    area_open_portal = {
        label = "开启传送门(Area)",
        include_patterns = {
            "mysteryarea_c.widgettree.commoncardbtn",
            "commoncardbtn",
            "开启传送门"
        },
        related_texts = {
            "开启传送门"
        },
        related_text_max_distance = 80,
        preferred_x = 1068.7905273438,
        preferred_y = 740.70751953125,
        preferred_max_distance = 100
    }
}
local F12_ACTIVE_TARGET = "rehab_guide"
local last_f3_relative_anchor = nil

nav.set_move_call_mouse_sync({
    enabled = MOVE_CALL_MOUSE_SYNC_ENABLED,
    mode = MOVE_CALL_MOUSE_SYNC_MODE,
    radius = MOVE_CALL_MOUSE_SYNC_RADIUS,
    swap_axes = MOVE_CALL_MOUSE_SYNC_SWAP_AXES,
    invert_x = MOVE_CALL_MOUSE_SYNC_INVERT_X,
    invert_y = MOVE_CALL_MOUSE_SYNC_INVERT_Y,
    skip_same_target_until_near = MOVE_CALL_MOUSE_SYNC_SKIP_SAME_TARGET_UNTIL_NEAR,
    near_target_distance = MOVE_CALL_MOUSE_SYNC_NEAR_TARGET_DISTANCE,
    target_change_distance = 12,
    padding_left = 80,
    padding_right = 80,
    padding_top = 80,
    padding_bottom = 120,
    log_errors = false
})

local function distance_2d(x1, y1, x2, y2)
    local dx = x1 - x2
    local dy = y1 - y2
    return math.sqrt(dx * dx + dy * dy)
end

local function round_to_digits(value, digits)
    local power = 10 ^ (digits or 0)
    if value >= 0 then
        return math.floor(value * power + 0.5) / power
    end
    return math.ceil(value * power - 0.5) / power
end

local function format_addr_hex(value)
    local number_value = tonumber(value)
    if number_value ~= nil then
        local integer_value = math.tointeger and math.tointeger(number_value) or nil
        if integer_value == nil and number_value == math.floor(number_value) then
            integer_value = math.floor(number_value)
        end
        if integer_value ~= nil then
            return string.format("0x%X", integer_value)
        end
    end
    return tostring(value or "")
end

local function find_f3_distance_text(button, texts)
    local button_x = tonumber(button and button.x)
    local button_y = tonumber(button and button.y)
    if button_x == nil or button_y == nil then
        return nil
    end

    for text_index, ctl in ipairs(texts or {}) do
        local text_x = tonumber(ctl.x)
        local text_y = tonumber(ctl.y)
        if text_x ~= nil and text_y ~= nil then
            local distance = distance_2d(button_x, button_y, text_x, text_y)
            if distance >= F3_DISTANCE_MIN and distance <= F3_DISTANCE_MAX then
                return ctl, distance, text_index
            end
        end
    end

    return nil
end

local function dump_f3_button_text_traversal()
    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return false, err
    end

    local buttons = snapshot.buttons or {}
    local texts = snapshot.texts or {}
    log.info(string.format(
        "F3 standalone traversal | buttons=%d texts=%d button_name=%s distance_range=(%.1f, %.1f)",
        #buttons,
        #texts,
        F3_BUTTON_NAME,
        F3_DISTANCE_MIN,
        F3_DISTANCE_MAX
    ))

    if #buttons == 0 then
        log.warn("F3 standalone traversal found no buttons.")
        return true
    end

    local named_buttons = 0
    local matched_pairs = 0

    for button_index, btn in ipairs(buttons) do
        if tostring(btn.name or "") == F3_BUTTON_NAME then
            named_buttons = named_buttons + 1
            local ctl, distance, text_index = find_f3_distance_text(btn, texts)
            if ctl then
                matched_pairs = matched_pairs + 1
                log.info(string.format(
                    "F3 traversal match[%d] | button_index=%d button_addr=%s button_name=%s button_x=%s button_y=%s text_index=%d text_addr=%s text_name=%s text=%s text_x=%s text_y=%s distance=%.6f",
                    matched_pairs,
                    button_index,
                    format_addr_hex(btn.addr),
                    tostring(btn.name or ""),
                    tostring(btn.x or ""),
                    tostring(btn.y or ""),
                    tonumber(text_index) or 0,
                    format_addr_hex(ctl.addr),
                    tostring(ctl.name or ""),
                    tostring(ctl.text or ""),
                    tostring(ctl.x or ""),
                    tostring(ctl.y or ""),
                    tonumber(distance) or 0
                ))
            end
        end
    end

    if named_buttons == 0 then
        log.warn("F3 standalone traversal found no button with the target name.")
    elseif matched_pairs == 0 then
        log.warn("F3 standalone traversal found target buttons, but no text in distance range.")
    else
        log.info(string.format(
            "F3 standalone traversal completed | named_buttons=%d matched_pairs=%d",
            named_buttons,
            matched_pairs
        ))
    end

    return true
end

local function text_contains_any(value, patterns)
    local text = tostring(value or ""):lower()
    if type(patterns) ~= "table" or #patterns == 0 then
        return true
    end

    for _, pattern in ipairs(patterns) do
        local needle = tostring(pattern or ""):lower()
        if needle ~= "" and text:find(needle, 1, true) then
            return true
        end
    end

    return false
end

local function find_f4_target_button(preset)
    preset = preset or F4_TARGET_PRESET

    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return nil, err
    end

    local anchor_text = tostring(preset.anchor_exact_text or "")
    local button_name = tostring(preset.button_name or "")
    local target_distance = tonumber(preset.distance_target) or 0
    local tolerance = tonumber(preset.distance_tolerance) or 0
    local round_digits = tonumber(preset.distance_round_digits) or 1
    local rounded_target_distance = round_to_digits(target_distance, round_digits)

    local texts = snapshot.texts or {}
    local buttons = snapshot.buttons or {}
    local anchors = {}

    for index, item in ipairs(texts) do
        if tostring(item.text or "") == anchor_text then
            anchors[#anchors + 1] = {
                index = index,
                item = item
            }
        end
    end

    log.info(string.format(
        "F4 anchor search | label=%s texts=%d buttons=%d anchor_text=%s button_name=%s target_distance=%.6f tolerance=%.3f rounded_target=%.1f",
        tostring(preset.label or ""),
        #texts,
        #buttons,
        anchor_text,
        button_name,
        target_distance,
        tolerance,
        rounded_target_distance
    ))

    for index, anchor in ipairs(anchors) do
        log.info(string.format(
            "F4 anchor match[%d] | text_index=%d addr=%s text=%s name=%s x=%s y=%s",
            index,
            tonumber(anchor.index) or 0,
            format_addr_hex(anchor.item.addr),
            tostring(anchor.item.text or ""),
            tostring(anchor.item.name or ""),
            tostring(anchor.item.x or ""),
            tostring(anchor.item.y or "")
        ))
    end

    if #anchors == 0 then
        return nil, "F4 anchor text not found: " .. anchor_text
    end

    local best = nil
    local nearest = nil
    local candidate_index = 0

    for _, anchor in ipairs(anchors) do
        local text_x = tonumber(anchor.item.x)
        local text_y = tonumber(anchor.item.y)
        if text_x ~= nil and text_y ~= nil then
            for button_index, btn in ipairs(buttons) do
                if tostring(btn.name or "") == button_name then
                    local button_x = tonumber(btn.x)
                    local button_y = tonumber(btn.y)
                    if button_x ~= nil and button_y ~= nil then
                        local distance = distance_2d(button_x, button_y, text_x, text_y)
                        local rounded_distance = round_to_digits(distance, round_digits)
                        local delta = math.abs(distance - target_distance)
                        local within_tolerance = delta <= tolerance
                        local rounded_match = rounded_distance == rounded_target_distance

                        candidate_index = candidate_index + 1
                        log.info(string.format(
                            "F4 candidate[%d] | button_index=%d button_addr=%s button_name=%s button_x=%s button_y=%s text_index=%d text_addr=%s text=%s text_x=%s text_y=%s distance=%.6f rounded_distance=%.1f delta=%.6f within_tolerance=%s rounded_match=%s",
                            candidate_index,
                            button_index,
                            format_addr_hex(btn.addr),
                            tostring(btn.name or ""),
                            tostring(btn.x or ""),
                            tostring(btn.y or ""),
                            tonumber(anchor.index) or 0,
                            format_addr_hex(anchor.item.addr),
                            tostring(anchor.item.text or ""),
                            tostring(anchor.item.x or ""),
                            tostring(anchor.item.y or ""),
                            distance,
                            rounded_distance,
                            delta,
                            tostring(within_tolerance),
                            tostring(rounded_match)
                        ))

                        if not nearest or delta < nearest.delta then
                            nearest = {
                                button = btn,
                                anchor = anchor.item,
                                text_index = anchor.index,
                                distance = distance,
                                rounded_distance = rounded_distance,
                                delta = delta
                            }
                        end

                        if within_tolerance or rounded_match then
                            if not best or delta < best.delta then
                                best = {
                                    button = btn,
                                    anchor = anchor.item,
                                    text_index = anchor.index,
                                    distance = distance,
                                    rounded_distance = rounded_distance,
                                    delta = delta
                                }
                            end
                        end
                    end
                end
            end
        end
    end

    if not best then
        if nearest then
            return nil, string.format(
                "F4 target button not found. nearest button_addr=%s text_addr=%s distance=%.6f rounded_distance=%.1f delta=%.6f",
                format_addr_hex(nearest.button.addr),
                format_addr_hex(nearest.anchor.addr),
                tonumber(nearest.distance) or 0,
                tonumber(nearest.rounded_distance) or 0,
                tonumber(nearest.delta) or 0
            )
        end
        return nil, "F4 target button not found."
    end

    return {
        anchor = best.anchor,
        text_index = best.text_index,
        button = best.button,
        raw_distance = tonumber(best.distance) or 0,
        rounded_distance = tonumber(best.rounded_distance) or 0,
        delta = tonumber(best.delta) or 0
    }
end

local function click_f4_target_button(preset)
    local result, err = find_f4_target_button(preset)
    if not result then
        return false, err
    end

    local ok, click_err = nav.control_click(result.button.addr)
    if not ok then
        return false, click_err
    end

    return true, {
        kind = "button",
        addr = result.button.addr,
        name = result.button.name,
        text = result.button.text,
        fullname = result.button.fullname,
        x = result.button.x,
        y = result.button.y,
        distance = result.raw_distance,
        rounded_distance = result.rounded_distance,
        delta = result.delta,
        anchor_x = result.anchor.x,
        anchor_y = result.anchor.y,
        anchor_text = result.anchor.text,
        text_index = result.text_index
    }
end

local function capture_f3_relative_anchor(cursor)
    if type(cursor) ~= "table" then
        return nil
    end

    local ratio_x = 0
    local ratio_y = 0

    if type(cursor.client_w) == "number" and cursor.client_w > 0 then
        ratio_x = cursor.client_x / cursor.client_w
    end
    if type(cursor.client_h) == "number" and cursor.client_h > 0 then
        ratio_y = cursor.client_y / cursor.client_h
    end

    last_f3_relative_anchor = {
        client_x = tonumber(cursor.client_x) or 0,
        client_y = tonumber(cursor.client_y) or 0,
        client_w = tonumber(cursor.client_w) or 0,
        client_h = tonumber(cursor.client_h) or 0,
        ratio_x = ratio_x,
        ratio_y = ratio_y,
        screen_x = tonumber(cursor.screen_x) or 0,
        screen_y = tonumber(cursor.screen_y) or 0
    }

    return last_f3_relative_anchor
end

local function resolve_f3_relative_anchor()
    local anchor = last_f3_relative_anchor
    if type(anchor) ~= "table" then
        return nil, "F3 relative anchor is empty. Press F3 on the target button first."
    end

    local hwnd, hwnd_err = nav.window_hwnd()
    if not hwnd then
        return nil, hwnd_err
    end

    if type(wnd) ~= "table" or type(wnd.client_rect) ~= "function" then
        return nil, "wnd.client_rect is not available."
    end

    local origin_x, origin_y, client_w, client_h = wnd.client_rect(hwnd)
    if type(origin_x) ~= "number"
        or type(origin_y) ~= "number"
        or type(client_w) ~= "number"
        or type(client_h) ~= "number"
    then
        return nil, "wnd.client_rect failed."
    end

    local use_ratio = type(anchor.ratio_x) == "number"
        and type(anchor.ratio_y) == "number"
        and client_w > 0
        and client_h > 0

    local client_x = tonumber(anchor.client_x) or 0
    local client_y = tonumber(anchor.client_y) or 0
    local mode = "client"

    if use_ratio then
        if math.abs(client_w - (tonumber(anchor.client_w) or 0)) > 1
            or math.abs(client_h - (tonumber(anchor.client_h) or 0)) > 1
        then
            client_x = anchor.ratio_x * client_w
            client_y = anchor.ratio_y * client_h
            mode = "ratio"
        end
    end

    client_x = math.max(0, math.min(client_w, client_x))
    client_y = math.max(0, math.min(client_h, client_y))

    return {
        hwnd = hwnd,
        origin_x = origin_x,
        origin_y = origin_y,
        client_w = client_w,
        client_h = client_h,
        client_x = client_x,
        client_y = client_y,
        screen_x = origin_x + client_x,
        screen_y = origin_y + client_y,
        ratio_x = tonumber(anchor.ratio_x) or 0,
        ratio_y = tonumber(anchor.ratio_y) or 0,
        source_client_x = tonumber(anchor.client_x) or 0,
        source_client_y = tonumber(anchor.client_y) or 0,
        source_client_w = tonumber(anchor.client_w) or 0,
        source_client_h = tonumber(anchor.client_h) or 0,
        mode = mode
    }
end

local function active_f12_target()
    return F12_TARGET_PRESETS[F12_ACTIVE_TARGET] or F12_TARGET_PRESETS.deposit
end

local function pick_f12_target_by_related_text(matches, preset)
    local related_texts = preset and preset.related_texts
    if type(matches) ~= "table" or #matches == 0 or type(related_texts) ~= "table" or #related_texts == 0 then
        return nil
    end

    local snapshot, err = nav.enum_ui()
    if not snapshot or type(snapshot.texts) ~= "table" then
        return nil
    end

    local max_distance = tonumber(preset.related_text_max_distance) or 90
    local best = nil
    local best_distance = nil
    local best_text = nil

    for _, match in ipairs(matches) do
        local mx = tonumber(match.x)
        local my = tonumber(match.y)
        if mx ~= nil and my ~= nil then
            for _, item in ipairs(snapshot.texts or {}) do
                local text = tostring(item.text or "")
                local x = tonumber(item.x)
                local y = tonumber(item.y)
                if x ~= nil and y ~= nil then
                    for _, wanted in ipairs(related_texts) do
                        if text == tostring(wanted) then
                            local dist = distance_2d(mx, my, x, y)
                            if dist <= max_distance and (best_distance == nil or dist < best_distance) then
                                best = match
                                best_distance = dist
                                best_text = text
                            end
                        end
                    end
                end
            end
        end
    end

    if best then
        best.pick_related_text = best_text
        best.pick_related_text_distance = best_distance
        return best
    end

    return nil
end

local function pick_f12_target(matches, preset)
    if type(matches) ~= "table" or #matches == 0 then
        return nil
    end

    local related_pick = pick_f12_target_by_related_text(matches, preset)
    if related_pick then
        return related_pick
    end

    local preferred_x = tonumber(preset and preset.preferred_x)
    local preferred_y = tonumber(preset and preset.preferred_y)
    if preferred_x == nil or preferred_y == nil then
        return matches[1]
    end

    local best = nil
    local best_distance = nil
    local max_distance = tonumber(preset.preferred_max_distance)

    for _, match in ipairs(matches) do
        local x = tonumber(match.x)
        local y = tonumber(match.y)
        if x ~= nil and y ~= nil then
            local dist = distance_2d(x, y, preferred_x, preferred_y)
            if (max_distance == nil or dist <= max_distance) and (best_distance == nil or dist < best_distance) then
                best = match
                best_distance = dist
            end
        end
    end

    if best then
        best.pick_distance = best_distance
        return best
    end

    return matches[1]
end

local function pick_target_by_preference(matches, preset, opts)
    opts = opts or {}

    local target = pick_f12_target(matches, preset)
    if not target then
        return nil
    end

    if opts.require_preferred then
        local preferred_x = tonumber(preset and preset.preferred_x)
        local preferred_y = tonumber(preset and preset.preferred_y)
        local pick_distance = tonumber(target.pick_distance)
        if preferred_x ~= nil and preferred_y ~= nil and pick_distance == nil then
            return nil
        end
    end

    return target
end

local function f2_client_origin(hwnd)
    if type(wnd) == "table" and type(wnd.client_rect) == "function" then
        local ox, oy = wnd.client_rect(hwnd)
        if type(ox) == "number" and type(oy) == "number" then
            return ox, oy
        end
    end

    return 0, 0
end

local function f2_move_and_click(hwnd, preset, screen_x, screen_y)
    if preset.perform_click == false then
        return human_mouse.move_to(screen_x, screen_y, {
            hwnd = hwnd,
            set_foreground = true,
            foreground_delay_ms = tonumber(preset.capture_foreground_delay_ms) or 60,
            mouse_mode = tostring(preset.click_mode or "api"),
            min_duration_ms = 300,
            max_duration_ms = 2000
        })
    end

    return human_mouse.move_and_click(screen_x, screen_y, {
        hwnd = hwnd,
        set_foreground = true,
        foreground_delay_ms = tonumber(preset.capture_foreground_delay_ms) or 60,
        mouse_mode = tostring(preset.click_mode or "api"),
        click_button = preset.click_button or "left",
        click_delay_ms = tonumber(preset.click_delay) or 50,
        before_click_extra_delay_ms = tonumber(preset.hover_delay_ms) or 0,
        min_duration_ms = 300,
        max_duration_ms = 2000
    })
end

local function f2_preview_and_post_click(hwnd, preset, resolved)
    local click_x = math.floor((tonumber(resolved.click_x) or 0) + 0.5)
    local click_y = math.floor((tonumber(resolved.click_y) or 0) + 0.5)
    local match_x = tonumber(resolved.match_x)
    local match_y = tonumber(resolved.match_y)
    if match_x == nil then
        match_x = click_x
    end
    if match_y == nil then
        match_y = click_y
    end

    local client_origin_x, client_origin_y = f2_client_origin(hwnd)
    local match_screen_x = math.floor(client_origin_x + match_x + 0.5)
    local match_screen_y = math.floor(client_origin_y + match_y + 0.5)
    local click_screen_x = math.floor(client_origin_x + click_x + 0.5)
    local click_screen_y = math.floor(client_origin_y + click_y + 0.5)

    local cursor_move_ok, cursor_move_err = f2_move_and_click(hwnd, preset, click_screen_x, click_screen_y)

    log.info(string.format(
        "F2 image resolved: source=%s capture_method=%s path=%s mode=%s threshold=%.2f score=%.4f template=%dx%d match_client=(%d,%d) match_screen=(%d,%d) click_client=(%d,%d) click_screen=(%d,%d) anchor_text=%s target_name=%s target_distance=%s move_cursor=%s move_err=%s capture=%s",
        tostring(resolved.source or ""),
        tostring(resolved.capture_method or ""),
        tostring(resolved.template_path or ""),
        tostring(resolved.match_mode or ""),
        tonumber(resolved.threshold) or 0,
        tonumber(resolved.score) or 0,
        tonumber(resolved.template_w) or 0,
        tonumber(resolved.template_h) or 0,
        math.floor(match_x + 0.5),
        math.floor(match_y + 0.5),
        match_screen_x,
        match_screen_y,
        click_x,
        click_y,
        click_screen_x,
        click_screen_y,
        tostring(resolved.anchor_text or ""),
        tostring(resolved.target_name or ""),
        tostring(resolved.target_distance or ""),
        tostring(cursor_move_ok),
        tostring(cursor_move_err or ""),
        tostring(resolved.debug_capture_path or "")
    ))

    if not cursor_move_ok then
        return false, tostring(cursor_move_err or "F2 front click failed.")
    end

    resolved.hwnd = hwnd
    resolved.match_x = math.floor(match_x + 0.5)
    resolved.match_y = math.floor(match_y + 0.5)
    resolved.match_screen_x = match_screen_x
    resolved.match_screen_y = match_screen_y
    resolved.click_x = click_x
    resolved.click_y = click_y
    resolved.click_screen_x = click_screen_x
    resolved.click_screen_y = click_screen_y
    resolved.cursor_move_ok = cursor_move_ok
    resolved.cursor_move_err = cursor_move_err
    return true, resolved
end

local function find_template_and_post_click(preset)
    if type(preset) ~= "table" then
        return false, "F2 preset is invalid."
    end

    if type(vision) ~= "table"
        or type(vision.capture) ~= "function"
        or type(vision.capture_window) ~= "function"
        or type(vision.load) ~= "function"
        or type(vision.find) ~= "function"
    then
        return false, "vision API is not available."
    end

    if type(mouse) ~= "table"
        or type(mouse.move_to) ~= "function"
        or type(mouse.click) ~= "function"
    then
        return false, "mouse.move_to/mouse.click is not available."
    end

    local hwnd, hwnd_err = nav.window_hwnd()
    if not hwnd then
        return false, hwnd_err
    end

    local template_path = resolve_project_path(trim(preset.template_path or ""))
    if not template_path or template_path == "" then
        return false, "F2 template path is empty."
    end

    local template = vision.load(template_path)
    if not template then
        return false, "F2 template load failed: " .. tostring(template_path)
    end

    if preset.capture_set_foreground ~= false
        and type(wnd) == "table"
        and type(wnd.set_foreground) == "function"
    then
        wnd.set_foreground(hwnd)
        sys.sleep(tonumber(preset.capture_foreground_delay_ms) or 60)
    end

    local capture = nil
    local capture_method = nil
    local client_x = nil
    local client_y = nil
    local client_w = nil
    local client_h = nil

    if type(wnd) == "table" and type(wnd.client_rect) == "function" then
        client_x, client_y, client_w, client_h = wnd.client_rect(hwnd)
        if type(client_x) == "number"
            and type(client_y) == "number"
            and type(client_w) == "number"
            and type(client_h) == "number"
            and client_w > 0
            and client_h > 0
        then
            capture = vision.capture(client_x, client_y, client_w, client_h)
            if valid_image(capture) then
                capture_method = "screen_region"
            else
                free_image(capture)
                capture = nil
            end
        end
    end

    if not capture then
        capture = vision.capture_window(hwnd, true)
        if valid_image(capture) then
            capture_method = "window_client"
        else
            free_image(capture)
            capture = nil
        end
    end

    if not capture then
        free_image(template)
        return false, "F2 capture failed: screen_region + capture_window both failed."
    end

    local threshold = tonumber(preset.template_threshold) or 0.84
    local x, y, score = vision.find(capture, template, threshold)
    local match_mode = "color"
    local match_threshold = threshold

    if (not x or not y) and type(vision.to_gray) == "function" then
        local gray_capture = vision.to_gray(capture)
        local gray_template = vision.to_gray(template)
        if gray_capture and gray_template then
            local gray_threshold = math.max(0.72, threshold - 0.06)
            x, y, score = vision.find(gray_capture, gray_template, gray_threshold)
            if x and y then
                match_mode = "gray"
                match_threshold = gray_threshold
            end
        end
        free_image(gray_capture)
        free_image(gray_template)
    end

    if not x or not y then
        local image_err = string.format(
            "F2 template not found: path=%s threshold=%.2f fallback=gray@%.2f capture_method=%s",
            tostring(template_path),
            threshold,
            math.max(0.72, threshold - 0.06),
            tostring(capture_method or "")
        )
        local fail_capture_path = nil
        if preset.save_capture_on_fail ~= false then
            fail_capture_path = save_f2_debug_capture(capture, "fail_latest")
        end
        free_image(template)
        free_image(capture)
        if fail_capture_path then
            image_err = image_err .. " capture=" .. tostring(fail_capture_path)
        end
        return false, image_err
    end

    local template_w = tonumber(template:width()) or 0
    local template_h = tonumber(template:height()) or 0
    local click_x = tonumber(x) or 0
    local click_y = tonumber(y) or 0
    local center_x = preset.click_center_x
    if center_x == nil then
        center_x = preset.click_center ~= false
    end
    local center_y = preset.click_center_y
    if center_y == nil then
        center_y = preset.click_center ~= false
    end
    if center_x then
        click_x = click_x + template_w * 0.5
    end
    if center_y then
        click_y = click_y + template_h * 0.5
    end

    click_x = math.floor(click_x + (tonumber(preset.click_offset_x) or 0) + 0.5)
    click_y = math.floor(click_y + (tonumber(preset.click_offset_y) or 0) + 0.5)
    local success_capture_path = nil
    if preset.save_capture_on_success == true then
        success_capture_path = save_f2_debug_capture(capture, "success_latest")
    end
    free_image(template)
    free_image(capture)
    return f2_preview_and_post_click(hwnd, preset, {
        source = "image",
        template_path = template_path,
        capture_method = capture_method,
        threshold = match_threshold,
        match_mode = match_mode,
        score = tonumber(score) or 0,
        template_w = template_w,
        template_h = template_h,
        match_x = tonumber(x) or 0,
        match_y = tonumber(y) or 0,
        click_x = click_x,
        click_y = click_y,
        debug_capture_path = success_capture_path
    })
end

local key_latch = {}
local active_route_points = ROUTE_POINTS
local active_route_name = "default route"
local route_running = false
local route_index = 0
local next_repath_at = 0
local next_init_retry_at = 0
local exit_latch = false
local initialized = false
local last_init_error = nil
local route_last_progress_x = nil
local route_last_progress_y = nil
local route_last_progress_at = 0

local function pressed_once(vk)
    local down = hotkey.is_pressed(vk)
    local fired = down and not key_latch[vk]
    key_latch[vk] = down
    return fired
end

local function route_point(index)
    return active_route_points[index]
end

local function move_to_route_point(index)
    local point = route_point(index)
    if not point then
        return false, "Route point does not exist."
    end

    local ok, err = nav.move_call(point.x, point.y)
    if not ok then
        return false, err
    end

    return true
end

local function clamp(value, min_value, max_value)
    if value < min_value then
        return min_value
    end
    if value > max_value then
        return max_value
    end
    return value
end

local function kickstart_route_point(index)
    local point = route_point(index)
    if not point then
        return false, "Route point does not exist."
    end

    local cur_x, cur_y = nav.player_pos()
    local hwnd, hwnd_err = nav.window_hwnd()
    if not hwnd then
        return false, hwnd_err
    end

    local _, _, client_w, client_h = wnd.client_rect(hwnd)
    if type(client_w) ~= "number" or type(client_h) ~= "number" then
        return false, "wnd.client_rect failed."
    end

    if cur_x == nil or cur_y == nil then
        return move_to_route_point(index)
    end

    local dx = point.x - cur_x
    local dy = point.y - cur_y
    local world_dist = distance_2d(cur_x, cur_y, point.x, point.y)

    if world_dist < 1 then
        return move_to_route_point(index)
    end

    local screen_dx = dx - dy
    local screen_dy = (dx + dy) * 0.5
    local screen_dist = math.sqrt(screen_dx * screen_dx + screen_dy * screen_dy)
    if screen_dist < 1 then
        return move_to_route_point(index)
    end

    local center_x = client_w * 0.5
    local center_y = client_h * 0.5
    local click_x = center_x + screen_dx / screen_dist * CUSTOM_ROUTE_KICK_CLICK_RADIUS
    local click_y = center_y + screen_dy / screen_dist * CUSTOM_ROUTE_KICK_CLICK_RADIUS

    click_x = math.floor(clamp(click_x, 80, client_w - 80))
    click_y = math.floor(clamp(click_y, 80, client_h - 120))

    local ok, err = nav.click_window_to_move(hwnd, click_x, click_y, {
        wait = false,
        delay = 60
    })
    if not ok then
        return false, err
    end

    sys.sleep(CUSTOM_ROUTE_KICK_DELAY_MS)
    return nav.move_call(point.x, point.y)
end

local function mark_route_progress_anchor(x, y)
    route_last_progress_x = x
    route_last_progress_y = y
    route_last_progress_at = sys.time()
end

local function stop_route(reason)
    route_running = false
    route_index = 0
    route_last_progress_x = nil
    route_last_progress_y = nil
    route_last_progress_at = 0
    if reason then
        log.info(reason)
    end
end

local function start_route(points, route_name)
    active_route_points = points
    active_route_name = route_name
    route_index = 1

    local point = route_point(route_index)
    local ok, err
    if points == CUSTOM_ROUTE_POINTS and CUSTOM_ROUTE_KICKSTART_ENABLED then
        ok, err = kickstart_route_point(route_index)
    else
        ok, err = move_to_route_point(route_index)
    end
    if not ok then
        route_running = false
        route_index = 0
        return false, err
    end

    local cur_x, cur_y = nav.player_pos()
    if cur_x ~= nil and cur_y ~= nil then
        mark_route_progress_anchor(cur_x, cur_y)
    else
        route_last_progress_x = nil
        route_last_progress_y = nil
        route_last_progress_at = sys.time()
    end

    route_running = true
    next_repath_at = sys.time() + REPATH_INTERVAL_MS
    log.info(string.format(
        "%s started %d/%d -> %.2f, %.2f",
        active_route_name,
        route_index,
        #active_route_points,
        point.x,
        point.y
    ))
    return true
end

log.info("Insert reserved for AvePoint automation")
log.info("Press F1 to enumerate nearby portals")
log.info("Press F2 to find image and move cursor to Ha/huishou.bmp")
log.info("Press F3 to run standalone button/text traversal for UIMysticMapItem ClickButton")
log.info("Press F4 to click MysticMapItem ClickButton by text 圣庭迷宫 and distance target")
log.info(string.format("Press F5 to run custom route (%d points)", #CUSTOM_ROUTE_POINTS))
log.info("Press F6 to dump button/image under mouse")
log.info("Press F7 to print current position")
log.info("Press F8 to dump visible controls/buttons with text")
log.info("Press F9 to click HardClickBtn")
log.info("Press F10 to dump bottom-left control candidates")
log.info("Press F11 to click button near heidi_L01")
log.info("Press F12 to find and click button by current relative anchor")
log.info("Press Ctrl+F12 to exit")
log.info(string.format(
    "MoveTo mouse sync: %s mode=%s radius=%d swap=%s invert_x=%s invert_y=%s repeat_skip=%s near=%d",
    MOVE_CALL_MOUSE_SYNC_ENABLED and "on" or "off",
    MOVE_CALL_MOUSE_SYNC_MODE,
    MOVE_CALL_MOUSE_SYNC_RADIUS,
    MOVE_CALL_MOUSE_SYNC_SWAP_AXES and "on" or "off",
    MOVE_CALL_MOUSE_SYNC_INVERT_X and "on" or "off",
    MOVE_CALL_MOUSE_SYNC_INVERT_Y and "on" or "off",
    MOVE_CALL_MOUSE_SYNC_SKIP_SAME_TARGET_UNTIL_NEAR and "on" or "off",
    MOVE_CALL_MOUSE_SYNC_NEAR_TARGET_DISTANCE
))
log.info("Waiting for torchlight API/game init...")

if not hotkey.is_running() then
    hotkey.start(10)
end

while true do
    local exit_down = hotkey.is_pressed(HOTKEY_EXIT_CTRL) and hotkey.is_pressed(HOTKEY_EXIT)
    if exit_down and not exit_latch then
        log.info("Exit hotkey pressed")
        break
    end
    exit_latch = exit_down

    if not initialized and sys.time() >= next_init_retry_at then
        local ok, err = nav.init(PROCESS_NAME, MODE)
        if ok then
            initialized = true
            last_init_error = nil
            log.info("Torch API initialized")

            local x, y, z, pos_err = nav.player_pos()
            if x ~= nil and y ~= nil then
                local map_ui, map_err = nav.get_map_ui_info()
                if type(map_ui) == "table" then
                    local parts = {
                        string.format("Current pos: %.2f, %.2f, %.2f", x, y, z or 0)
                    }
                    if type(map_ui.current_map) == "table" then
                        parts[#parts + 1] = "map=" .. tostring(map_ui.current_map.text or "")
                    end
                    if type(map_ui.monster_level) == "table" then
                        parts[#parts + 1] = "monster_level=" .. tostring(map_ui.monster_level.text or "")
                    end
                    if type(map_ui.remaining_enemies) == "table" then
                        parts[#parts + 1] = "remaining_enemies=" .. tostring(map_ui.remaining_enemies.text or "")
                    end
                    log.info(table.concat(parts, " | "))
                    if type(map_ui.current_map) ~= "table"
                        and type(map_ui.debug_candidates) == "table"
                        and #map_ui.debug_candidates > 0
                    then
                        log.warn("Current map candidates: " .. table.concat(map_ui.debug_candidates, " | "))
                    end
                else
                    log.info(string.format("Current pos: %.2f, %.2f, %.2f", x, y, z or 0))
                    log.warn("Read current map failed: " .. tostring(map_err))
                end
            else
                log.warn("Read position failed: " .. tostring(pos_err))
            end
        else
            if err ~= last_init_error then
                log.warn("Torch init pending: " .. tostring(err))
                last_init_error = err
            end
            next_init_retry_at = sys.time() + INIT_RETRY_MS
        end
    end

    if pressed_once(HOTKEY_PRINT_POS) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local cur_x, cur_y, cur_z, cur_err = nav.player_pos()
            if cur_x ~= nil and cur_y ~= nil then
                local map_ui, map_err = nav.get_map_ui_info()
                if type(map_ui) == "table" then
                    local parts = {
                        string.format("Current pos: %.2f, %.2f, %.2f", cur_x, cur_y, cur_z or 0)
                    }
                    if type(map_ui.current_map) == "table" then
                        parts[#parts + 1] = "map=" .. tostring(map_ui.current_map.text or "")
                    end
                    if type(map_ui.monster_level) == "table" then
                        parts[#parts + 1] = "monster_level=" .. tostring(map_ui.monster_level.text or "")
                    end
                    if type(map_ui.remaining_enemies) == "table" then
                        parts[#parts + 1] = "remaining_enemies=" .. tostring(map_ui.remaining_enemies.text or "")
                    end
                    log.info(table.concat(parts, " | "))
                    if type(map_ui.current_map) ~= "table"
                        and type(map_ui.debug_candidates) == "table"
                        and #map_ui.debug_candidates > 0
                    then
                        log.warn("Current map candidates: " .. table.concat(map_ui.debug_candidates, " | "))
                    end
                else
                    log.info(string.format("Current pos: %.2f, %.2f, %.2f", cur_x, cur_y, cur_z or 0))
                    log.warn("Read current map failed: " .. tostring(map_err))
                end
            else
                log.warn("Read position failed: " .. tostring(cur_err))
            end
        end
    end

    if pressed_once(HOTKEY_PICK_CONTROL) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local ok, result = nav.dump_control_at_cursor({
                include_texts = false,
                max_distance = 180
            })
            if not ok then
                log.error("Cursor control dump failed: " .. tostring(result))
            end
        end
    end

    if pressed_once(HOTKEY_CLICK_ANNIVERSARY_REWARD) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local ok, result = find_template_and_post_click(F2_TARGET_PRESET)
            if ok then
                log.info(string.format(
                    "F2 image moved: source=%s capture_method=%s path=%s mode=%s threshold=%.2f score=%.4f match_client=(%d,%d) match_screen=(%d,%d) click_client=(%d,%d) click_screen=(%d,%d) anchor_text=%s target_name=%s move_cursor=%s hwnd=%s capture=%s",
                    tostring(result.source or ""),
                    tostring(result.capture_method or ""),
                    tostring(result.template_path or ""),
                    tostring(result.match_mode or ""),
                    tonumber(result.threshold) or 0,
                    tonumber(result.score) or 0,
                    tonumber(result.match_x) or 0,
                    tonumber(result.match_y) or 0,
                    tonumber(result.match_screen_x) or 0,
                    tonumber(result.match_screen_y) or 0,
                    tonumber(result.click_x) or 0,
                    tonumber(result.click_y) or 0,
                    tonumber(result.click_screen_x) or 0,
                    tonumber(result.click_screen_y) or 0,
                    tostring(result.anchor_text or ""),
                    tostring(result.target_name or ""),
                    tostring(result.cursor_move_ok),
                    tostring(result.hwnd or ""),
                    tostring(result.debug_capture_path or "")
                ))
            else
                log.error("F2 template move failed: " .. tostring(result))
            end
        end
    end

    if pressed_once(HOTKEY_DUMP_SELECTED_BUTTON) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local ok, traversal_err = dump_f3_button_text_traversal()
            if not ok then
                log.error("F3 standalone traversal failed: " .. tostring(traversal_err))
            end
            goto f3_traversal_done
            local cursor, cursor_err = nav.cursor_client_pos()
            if not cursor then
                log.error("F3 exact cursor fetch failed: " .. tostring(cursor_err))
            else
                local anchor = capture_f3_relative_anchor(cursor)
                log.info(string.format(
                    "F3 current cursor point | client=(%.2f, %.2f) size=(%.2f, %.2f) ratio=(%.6f, %.6f) screen=(%.2f, %.2f)",
                    tonumber(anchor.client_x) or 0,
                    tonumber(anchor.client_y) or 0,
                    tonumber(anchor.client_w) or 0,
                    tonumber(anchor.client_h) or 0,
                    tonumber(anchor.ratio_x) or 0,
                    tonumber(anchor.ratio_y) or 0,
                    tonumber(anchor.screen_x) or 0,
                    tonumber(anchor.screen_y) or 0
                ))

                log.info(string.format(
                    "F3 exact cursor dump request | mode=%s max_distance=%.0f limit=%d include_buttons=true include_texts=true include_images=true",
                    "current",
                    F3_CURSOR_MAX_DISTANCE,
                    F3_CURSOR_LIMIT
                ))

                local ok, dump_err = nav.dump_controls_at_cursor({
                    include_buttons = true,
                    include_images = true,
                    include_texts = true,
                    max_distance = F3_CURSOR_MAX_DISTANCE,
                    limit = F3_CURSOR_LIMIT,
                    include_complex_fields = true,
                    dump_item = true,
                    dump_depth = 5,
                    dump_table_limit = 64
                })
                if not ok then
                    log.error("F3 exact cursor fetch failed: " .. tostring(dump_err))
                end
            end
            goto f3_done
            local ok, result = nav.dump_controls_by_match({
                header = "Objects containing text 亚人村落",
                include_buttons = true,
                include_texts = true,
                include_images = false,
                include_patterns = {
                    F3_TARGET_TEXT
                }
            })
            if not ok then
                log.error("F3 text match dump failed: " .. tostring(result))
            end
        end
    end

    ::f3_traversal_done::
    ::f3_done::

    if pressed_once(HOTKEY_DUMP_CURRENT_SELECTED_API_F1) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local ok, err = dump_nearby_portals()
            if not ok then
                log.error("Nearby portal dump failed: " .. tostring(err))
            end
        end
    end

    if pressed_once(HOTKEY_DUMP_CURRENT_SELECTED_API) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local ok, result = click_f4_target_button(F4_TARGET_PRESET)
            if not ok then
                log.error("F4 target click failed: " .. tostring(result))
            else
                log.info(string.format(
                    "F4 target clicked: kind=%s addr=%s name=%s text=%s fullname=%s x=%s y=%s anchor_text=%s text_index=%s anchor_pos=(%.2f, %.2f) distance=%.6f rounded_distance=%.1f delta=%.6f",
                    tostring(result.kind or ""),
                    tostring(result.addr or ""),
                    tostring(result.name or ""),
                    tostring(result.text or ""),
                    tostring(result.fullname or ""),
                    tostring(result.x or ""),
                    tostring(result.y or ""),
                    tostring(result.anchor_text or ""),
                    tostring(result.text_index or ""),
                    tonumber(result.anchor_x) or 0,
                    tonumber(result.anchor_y) or 0,
                    tonumber(result.distance) or 0,
                    tonumber(result.rounded_distance) or 0,
                    tonumber(result.delta) or 0
                ))
            end
        end
    end

    if pressed_once(HOTKEY_DUMP_VISIBLE_CONTROLS) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local ok, err = nav.dump_visible_controls({
                include_buttons = true,
                include_texts = true,
                include_images = false,
                header = "Visible controls on current page"
            })
            if not ok then
                log.error("Visible control dump failed: " .. tostring(err))
            end
        end
    end

    if pressed_once(HOTKEY_CLICK_BOTTOM_LEFT) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local ok, result = nav.click_button_by_match({
                include_patterns = HARD_CLICK_PATTERNS
            })
            if ok then
                log.info(string.format(
                    "HardClickBtn clicked: kind=%s addr=%s name=%s text=%s fullname=%s x=%s y=%s",
                    tostring(result.kind or ""),
                    tostring(result.addr or ""),
                    tostring(result.name or ""),
                    tostring(result.text or ""),
                    tostring(result.fullname or ""),
                    tostring(result.x or ""),
                    tostring(result.y or "")
                ))
            else
                log.error("HardClickBtn click failed: " .. tostring(result))
            end
        end
    end

    if pressed_once(HOTKEY_DUMP_BOTTOM_LEFT) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local ok, err = nav.dump_bottom_left_candidates()
            if not ok then
                log.error("Bottom-left candidate dump failed: " .. tostring(err))
            end
        end
    end

    if pressed_once(HOTKEY_CLICK_COST_ICON) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local ok, result = nav.click_control_by_match({
                include_buttons = false,
                include_images = true,
                include_texts = false,
                include_patterns = F11_TARGET_PATTERNS,
                prefer_button_neighbor = true,
                neighbor_max_distance = 160
            })
            if ok then
                log.info(string.format(
                    "F11 target clicked: kind=%s addr=%s name=%s text=%s fullname=%s x=%s y=%s anchor_kind=%s anchor_fullname=%s",
                    tostring(result.kind or ""),
                    tostring(result.addr or ""),
                    tostring(result.name or ""),
                    tostring(result.text or ""),
                    tostring(result.fullname or ""),
                    tostring(result.x or ""),
                    tostring(result.y or ""),
                    tostring(result.anchor_kind or ""),
                    tostring(result.anchor_fullname or "")
                ))
            else
                log.error("F11 target click failed: " .. tostring(result))
            end
        end
    end

    if pressed_once(HOTKEY_DUMP_PAGE_TEXT_CONTROLS) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            local cursor, cursor_err = nav.cursor_client_pos()
            if not cursor then
                log.error("F12 relative anchor fetch failed: " .. tostring(cursor_err))
            else
                local anchor = capture_f3_relative_anchor(cursor)
                log.info(string.format(
                    "F12 current relative anchor | client=(%.2f, %.2f) size=(%.2f, %.2f) ratio=(%.6f, %.6f) screen=(%.2f, %.2f)",
                    tonumber(anchor.client_x) or 0,
                    tonumber(anchor.client_y) or 0,
                    tonumber(anchor.client_w) or 0,
                    tonumber(anchor.client_h) or 0,
                    tonumber(anchor.ratio_x) or 0,
                    tonumber(anchor.ratio_y) or 0,
                    tonumber(anchor.screen_x) or 0,
                    tonumber(anchor.screen_y) or 0
                ))

                local matches, fetch_err = nav.find_controls_at_point(anchor.client_x, anchor.client_y, {
                    include_buttons = true,
                    include_images = false,
                    include_texts = false,
                    max_distance = 140,
                    limit = 6
                })
                if not matches then
                    log.error("F12 relative anchor fetch failed: " .. tostring(fetch_err))
                else
                    local result = matches[1]
                    log.info(string.format(
                        "F12 relative anchor fetch | controls=%d mode=%s anchor_client=(%.2f, %.2f) source_client=(%.2f, %.2f) size=(%.2f, %.2f) ratio=(%.6f, %.6f) screen=(%.2f, %.2f)",
                        #matches,
                        "current",
                        tonumber(anchor.client_x) or 0,
                        tonumber(anchor.client_y) or 0,
                        tonumber(anchor.client_x) or 0,
                        tonumber(anchor.client_y) or 0,
                        tonumber(anchor.client_w) or 0,
                        tonumber(anchor.client_h) or 0,
                        tonumber(anchor.ratio_x) or 0,
                        tonumber(anchor.ratio_y) or 0,
                        tonumber(anchor.screen_x) or 0,
                        tonumber(anchor.screen_y) or 0
                    ))

                    local click_ok, click_err = nav.control_click(result.addr)
                    if click_ok then
                        log.info(string.format(
                            "F12 anchored button clicked: kind=%s addr=%s text=%s name=%s fullname=%s x=%s y=%s distance=%s",
                            tostring(result.kind or ""),
                            tostring(result.addr or ""),
                            tostring(result.text or ""),
                            tostring(result.name or ""),
                            tostring(result.fullname or ""),
                            tostring(result.x or ""),
                            tostring(result.y or ""),
                            tostring(result.distance or "")
                        ))
                    else
                        log.error("F12 anchored button click failed: " .. tostring(click_err))
                    end
                end
            end
        end
    end

    if pressed_once(HOTKEY_CUSTOM_ROUTE) then
        if not initialized then
            log.warn("Torch API not ready yet")
        else
            if route_running and active_route_points == CUSTOM_ROUTE_POINTS then
                stop_route("Custom route stopped")
            else
                local ok, err = start_route(CUSTOM_ROUTE_POINTS, "Custom route")
                if not ok then
                    log.error("Custom route start failed: " .. tostring(err))
                end
            end
        end
    end

    if route_running then
        local point = route_point(route_index)
        if not point then
            stop_route(active_route_name .. " completed")
        else
            local cur_x, cur_y, _, cur_err = nav.player_pos()
            if cur_x ~= nil and cur_y ~= nil then
                if route_last_progress_x == nil or route_last_progress_y == nil then
                    mark_route_progress_anchor(cur_x, cur_y)
                elseif distance_2d(cur_x, cur_y, route_last_progress_x, route_last_progress_y) >= ROUTE_PROGRESS_DISTANCE then
                    mark_route_progress_anchor(cur_x, cur_y)
                end

                local dist = distance_2d(cur_x, cur_y, point.x, point.y)
                local custom_route_stuck = active_route_points == CUSTOM_ROUTE_POINTS
                    and CUSTOM_ROUTE_KICKSTART_ENABLED
                    and route_last_progress_at > 0
                    and (sys.time() - route_last_progress_at) >= CUSTOM_ROUTE_STUCK_TIMEOUT_MS
                    and dist > ARRIVE_TOLERANCE

                if custom_route_stuck then
                    local ok, err = kickstart_route_point(route_index)
                    if ok then
                        mark_route_progress_anchor(cur_x, cur_y)
                        next_repath_at = sys.time() + REPATH_INTERVAL_MS
                        log.info(string.format(
                            "%s kickstarted point %d/%d -> %.2f, %.2f",
                            active_route_name, route_index, #active_route_points, point.x, point.y
                        ))
                    else
                        stop_route(nil)
                        log.error(active_route_name .. " kickstart failed: " .. tostring(err))
                    end
                elseif dist <= ARRIVE_TOLERANCE then
                    if route_index >= #active_route_points then
                        stop_route(string.format("%s completed -> %.2f, %.2f", active_route_name, point.x, point.y))
                    else
                        route_index = route_index + 1
                        point = route_point(route_index)
                        local ok, err = move_to_route_point(route_index)
                        if ok then
                            mark_route_progress_anchor(cur_x, cur_y)
                            next_repath_at = sys.time() + REPATH_INTERVAL_MS
                            log.info(string.format(
                                "%s next point %d/%d -> %.2f, %.2f",
                                active_route_name, route_index, #active_route_points, point.x, point.y
                            ))
                        else
                            stop_route(nil)
                            log.error(active_route_name .. " next point failed: " .. tostring(err))
                        end
                    end
                elseif sys.time() >= next_repath_at then
                    local ok, err = move_to_route_point(route_index)
                    if ok then
                        next_repath_at = sys.time() + REPATH_INTERVAL_MS
                    else
                        stop_route(nil)
                        log.error(active_route_name .. " repath failed: " .. tostring(err))
                    end
                end
            else
                log.warn("Track position failed: " .. tostring(cur_err))
                stop_route(nil)
            end
        end
    end

    sys.sleep(POLL_INTERVAL_MS)
end

hotkey.stop()
