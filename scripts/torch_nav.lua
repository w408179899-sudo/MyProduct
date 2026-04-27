local nav = {
    pid = nil,
    mode = nil,
    init_api = nil,
    game_api = nil,
    target_ref = nil,
    last_move_call_target = nil,
    last_cursor_match = nil,
    last_cursor_matches = nil,
    move_call_mouse_sync = {
        enabled = false,
        mode = "direction",
        radius = 140,
        swap_axes = false,
        invert_x = false,
        invert_y = false,
        skip_same_target_until_near = false,
        near_target_distance = 160,
        target_change_distance = 12,
        padding_left = 80,
        padding_right = 80,
        padding_top = 80,
        padding_bottom = 120,
        log_errors = false
    }
}

local function loadfile_with_bytecode_fallback(path, label)
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

local function as_number(value)
    if type(value) == "number" then
        return value
    end
    if type(value) == "string" then
        return tonumber(value)
    end
    return nil
end

local function as_boolean(value)
    if type(value) == "boolean" then
        return value
    end

    local numeric = as_number(value)
    if numeric ~= nil then
        return numeric ~= 0
    end

    local text = tostring(value or ""):lower()
    if text == "true" then
        return true
    end
    if text == "false" then
        return false
    end

    return nil
end

local function pick_number(tbl, keys)
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

local function extract_position(info)
    if type(info) ~= "table" then
        return nil, nil, nil
    end

    local x = pick_number(info, { "x", "X", "posX", "PosX", "worldX", "WorldX" })
    local y = pick_number(info, { "y", "Y", "posY", "PosY", "worldY", "WorldY" })
    local z = pick_number(info, { "z", "Z", "posZ", "PosZ", "worldZ", "WorldZ", "angle" })

    if x ~= nil and y ~= nil then
        return x, y, z
    end

    for _, key in ipairs({ "pos", "position", "coord", "coords", "point" }) do
        local pos = info[key]
        if type(pos) == "table" then
            x = pick_number(pos, { "x", "X", "posX", "PosX", "worldX", "WorldX" })
            y = pick_number(pos, { "y", "Y", "posY", "PosY", "worldY", "WorldY" })
            z = pick_number(pos, { "z", "Z", "posZ", "PosZ", "worldZ", "WorldZ" })
            if x ~= nil and y ~= nil then
                return x, y, z
            end
        end
    end

    return nil, nil, nil
end

local function distance_2d(x1, y1, x2, y2)
    local dx = x1 - x2
    local dy = y1 - y2
    return math.sqrt(dx * dx + dy * dy)
end

local function clamp(value, min_value, max_value)
    if min_value ~= nil and value < min_value then
        return min_value
    end
    if max_value ~= nil and value > max_value then
        return max_value
    end
    return value
end

local function safe_items(items)
    if type(items) == "table" then
        return items
    end
    return {}
end

local function quiet_call(fn, ...)
    if type(fn) ~= "function" then
        return nil, "target is not callable"
    end

    local old_print = print
    local old_log_info = type(log) == "table" and log.info or nil

    print = function() end
    if old_log_info then
        log.info = function() end
    end

    local results = table.pack(pcall(fn, ...))

    print = old_print
    if old_log_info then
        log.info = old_log_info
    end

    if not results[1] then
        return nil, results[2]
    end

    return table.unpack(results, 2, results.n)
end

local function resolve_pid(target)
    if type(target) == "number" then
        return target
    end

    if type(target) == "string" and target ~= "" then
        local target_name = target:lower()
        local list = proc.list() or {}

        for _, item in ipairs(list) do
            if type(item) == "table" and type(item.pid) == "number" then
                local name = tostring(item.name or ""):lower()
                if name == target_name then
                    return item.pid
                end
            end
        end
    end

    return nil
end

local function normalize_game_api()
    if type(nav.game_api) ~= "table" then
        return
    end

    if nav.game_api.move_EMoveCtrl == nil then
        -- Game version offset for EMoveCtrl used by MoveTo.
        nav.game_api.move_EMoveCtrl = 0x4D8
    end
end

local function adopt_module(mod)
    if type(mod) ~= "table" then
        return false
    end

    if not nav.init_api then
        if type(mod.InitGameinfo) == "function" then
            nav.init_api = mod
        elseif type(mod.init) == "table" and type(mod.init.InitGameinfo) == "function" then
            nav.init_api = mod.init
        end
    end

    if not nav.game_api then
        if type(mod.MoveTo) == "function" and type(mod.GetPlayerinfo) == "function" then
            nav.game_api = mod
        elseif type(mod.M) == "table" and type(mod.M.MoveTo) == "function" and type(mod.M.GetPlayerinfo) == "function" then
            nav.game_api = mod.M
        end
    end

    normalize_game_api()
    return nav.init_api ~= nil and nav.game_api ~= nil
end

local function discover_from_globals()
    if type(init) == "table" and type(init.InitGameinfo) == "function" then
        nav.init_api = init
    end

    if type(M) == "table" and type(M.MoveTo) == "function" and type(M.GetPlayerinfo) == "function" then
        nav.game_api = M
    end

    if nav.init_api and nav.game_api then
        return true
    end

    for _, value in pairs(_G) do
        if type(value) == "table" then
            if not nav.init_api and type(value.InitGameinfo) == "function" then
                nav.init_api = value
            end

            if not nav.game_api and type(value.MoveTo) == "function" and type(value.GetPlayerinfo) == "function" then
                nav.game_api = value
            end
        end

        if nav.init_api and nav.game_api then
            return true
        end
    end

    return nav.init_api ~= nil and nav.game_api ~= nil
end

local function try_require(name)
    local ok, mod = pcall(require, name)
    if not ok then
        return false
    end

    return adopt_module(mod)
end

local function ensure_api()
    if nav.init_api and nav.game_api then
        return true
    end

    if discover_from_globals() then
        normalize_game_api()
        return true
    end

    if try_require("data") or try_require("scripts.data") then
        normalize_game_api()
        return true
    end

    if discover_from_globals() then
        normalize_game_api()
        return true
    end

    return false, "Torch API module not found (data.luac load attempted)."
end

function nav.reset(opts)
    opts = opts or {}

    nav.pid = nil
    nav.mode = nil
    nav.target_ref = nil
    nav.last_move_call_target = nil
    nav.last_cursor_match = nil
    nav.last_cursor_matches = nil

    if opts.reload_api == true then
        nav.init_api = nil
        nav.game_api = nil

        if type(package) == "table" and type(package.loaded) == "table" then
            package.loaded["data"] = nil
            package.loaded["scripts.data"] = nil
        end

        if type(_G) == "table" then
            _G.init = nil
            _G.M = nil
        end
    end
end

function nav.init(target, mode)
    local pid = resolve_pid(target)
    if not pid or pid <= 0 then
        nav.reset()
        return false, "Target process not found."
    end

    local old_pid = tonumber(nav.pid)
    local needs_reload = false
    if old_pid ~= nil and old_pid > 0 then
        if old_pid ~= pid then
            needs_reload = true
        elseif type(proc) == "table" and type(proc.exists) == "function" and not proc.exists(old_pid) then
            needs_reload = true
        end
    end

    if needs_reload then
        nav.reset({
            reload_api = true
        })
    end

    local ok, err = ensure_api()
    if not ok then
        return false, err
    end

    local use_mode = mode or "driver"
    local init_ok, init_err = quiet_call(nav.init_api.InitGameinfo, pid, use_mode)
    if not init_ok then
        if not needs_reload then
            nav.reset({
                reload_api = true
            })

            ok, err = ensure_api()
            if not ok then
                return false, err
            end

            init_ok, init_err = quiet_call(nav.init_api.InitGameinfo, pid, use_mode)
        end

        if not init_ok then
            return false, string.format(
                "InitGameinfo failed | pid=%d mode=%s err=%s",
                pid,
                tostring(use_mode),
                tostring(init_err or "InitGameinfo failed.")
            )
        end
    end

    nav.pid = pid
    nav.mode = use_mode
    nav.target_ref = target
    normalize_game_api()
    return true
end

function nav.is_initialized()
    return nav.pid ~= nil
        and proc.exists(nav.pid)
        and nav.init_api ~= nil
        and nav.game_api ~= nil
end

function nav.ensure_initialized(target, mode)
    if nav.is_initialized() then
        return true
    end

    local init_target = target or nav.target_ref or nav.pid
    if not init_target then
        return false, "Target process is not provided."
    end

    return nav.init(init_target, mode or nav.mode or "driver")
end

function nav.player_info()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    local info, info_err = quiet_call(nav.game_api.GetPlayerinfo)
    if not info then
        return nil, info_err or "GetPlayerinfo failed."
    end

    return info
end

function nav.is_main_interface()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    if type(nav.game_api.IsMainInterface) ~= "function" then
        return nil, "IsMainInterface is not available."
    end

    local value, value_err = quiet_call(nav.game_api.IsMainInterface)
    if value == nil then
        return nil, value_err or "IsMainInterface failed."
    end

    local result = as_boolean(value)
    if result == nil then
        return nil, "IsMainInterface returned invalid data."
    end

    return result
end

function nav.is_loading()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    if type(nav.game_api.Isloading) ~= "function" then
        return nil, "Isloading is not available."
    end

    local value, value_err = quiet_call(nav.game_api.Isloading)
    if value == nil then
        return nil, value_err or "Isloading failed."
    end

    local result = as_boolean(value)
    if result == nil then
        return nil, "Isloading returned invalid data."
    end

    return result
end

function nav.get_main_task_pos()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    if type(nav.game_api.GetMainTaskPos) ~= "function" then
        return nil, "GetMainTaskPos is not available."
    end

    local point, point_err = quiet_call(nav.game_api.GetMainTaskPos)
    if point == nil then
        return nil, point_err or "GetMainTaskPos failed."
    end
    if type(point) ~= "table" then
        return nil, "GetMainTaskPos returned invalid data."
    end

    local x, y, z = extract_position(point)
    if x == nil or y == nil then
        return nil, "GetMainTaskPos returned invalid coordinates."
    end
    if math.abs(x) < 0.001 and math.abs(y) < 0.001 then
        return nil, "GetMainTaskPos returned zero coordinates."
    end

    return {
        x = x,
        y = y,
        z = z,
        raw = point
    }
end

function nav.get_main_task_path()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    if type(nav.game_api.GetMainTaskPath) ~= "function" then
        return nil, "GetMainTaskPath is not available."
    end

    local raw_path, path_err = quiet_call(nav.game_api.GetMainTaskPath)
    if raw_path == nil then
        return nil, path_err or "GetMainTaskPath failed."
    end
    if type(raw_path) ~= "table" then
        return nil, "GetMainTaskPath returned invalid data."
    end

    local direct_x, direct_y, direct_z = extract_position(raw_path)
    if direct_x ~= nil and direct_y ~= nil then
        return {
            {
                x = direct_x,
                y = direct_y,
                z = direct_z,
                index = 1,
                raw = raw_path
            }
        }
    end

    local list = raw_path
    if #list == 0 then
        for _, key in ipairs({ "points", "path", "list", "items" }) do
            if type(raw_path[key]) == "table" then
                list = raw_path[key]
                break
            end
        end
    end

    local points = {}
    for index, point in ipairs(safe_items(list)) do
        if type(point) == "table" then
            local x, y, z = extract_position(point)
            if x ~= nil and y ~= nil then
                points[#points + 1] = {
                    x = x,
                    y = y,
                    z = z,
                    index = index,
                    raw = point
                }
            end
        end
    end

    if #points == 0 then
        return nil, "GetMainTaskPath returned no usable points."
    end

    return points
end

function nav.extract_position(info)
    return extract_position(info)
end

function nav.enum_ui()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    local buttons = {}
    local texts = {}
    local images = {}

    if type(nav.game_api.EnumCButton) == "function" then
        buttons = safe_items(quiet_call(nav.game_api.EnumCButton))
    end

    if type(nav.game_api.EnumCText) == "function" then
        texts = safe_items(quiet_call(nav.game_api.EnumCText))
    end

    if type(nav.game_api.EnumCImage) == "function" then
        images = safe_items(quiet_call(nav.game_api.EnumCImage))
    end

    return {
        buttons = buttons,
        texts = texts,
        images = images
    }
end

function nav.get_task_panel_info(snapshot)
    if type(snapshot) ~= "table" then
        local ui, err = nav.enum_ui()
        if type(ui) ~= "table" then
            return nil, err or "enum_ui failed."
        end
        snapshot = ui
    end

    local function trim_text(value)
        return tostring(value or ""):gsub("^%s+", ""):gsub("%s+$", "")
    end

    local function normalize_text(value)
        return trim_text(value):lower()
    end

    local function classify_task_kind(raw_text)
        local text = trim_text(raw_text)
        if text:match("^主线%s*") then
            return "主线"
        end
        if text:match("^支线%s*") then
            return "支线"
        end
        if text:match("^赛季%s*") then
            return "赛季"
        end
        return ""
    end

    local function looks_like_task_status_text(raw_text)
        local text = trim_text(raw_text)
        return text == "新" or text == "完成"
    end

    local function normalize_task_title(raw_text)
        local text = trim_text(raw_text)
        if text == "" then
            return nil
        end
        local kind = classify_task_kind(text)
        text = trim_text(text:gsub("^主线%s*", ""))
        text = trim_text(text:gsub("^支线%s*", ""))
        text = trim_text(text:gsub("^赛季%s*", ""))
        text = trim_text(text:gsub("^任务%s*", ""))
        text = trim_text(text:gsub("^目标%s*", ""))
        text = trim_text(text:gsub("^目標%s*", ""))
        if text == ""
            or text == "主线"
            or text == "支线"
            or text == "任务"
            or text == "目标"
            or text == "追踪"
        then
            return nil
        end
        if text:match("^[%d%s%/%-%:%+]+$") then
            return nil
        end
        local lower_text = text:lower()
        if lower_text:match("^%d+%s*ms$")
            or lower_text:match("^%d+%s*fps$")
            or lower_text:find("fps", 1, true) ~= nil
        then
            return nil
        end
        return text, kind
    end

    local function task_item_anchor_score(item)
        local haystack = table.concat({
            tostring(item and item.name or ""),
            tostring(item and item.Fullname or item and item.fullname or "")
        }, " "):lower()
        if haystack:find("taskitem_c.widgettree.taskbtn", 1, true) ~= nil
            or haystack:find("widgettree.taskbtn", 1, true) ~= nil
        then
            return 0
        end
        if haystack:find("taskitem_c.widgettree.uiimage", 1, true) ~= nil then
            return 12
        end
        if haystack:find("taskitem_c.widgettree.fullimg", 1, true) ~= nil then
            return 18
        end
        if haystack:find("taskitem_c.widgettree.padding_dummy", 1, true) ~= nil then
            return 24
        end
        if haystack:find("taskitem_c.widgettree.shuangguang", 1, true) ~= nil then
            return 28
        end
        if haystack:find("taskitem_c.widgettree.", 1, true) ~= nil then
            return 36
        end
        return nil
    end

    local task_anchors = {}
    local function push_task_anchor(kind, item)
        local x = as_number(item.x)
        local y = as_number(item.y)
        local anchor_score = task_item_anchor_score(item)
        if x ~= nil and y ~= nil and anchor_score ~= nil then
            task_anchors[#task_anchors + 1] = {
                kind = kind,
                x = x,
                y = y,
                item = item,
                anchor_score = anchor_score
            }
        end
    end

    for _, item in ipairs(snapshot.buttons or {}) do
        push_task_anchor("button", item)
    end
    for _, item in ipairs(snapshot.images or {}) do
        push_task_anchor("image", item)
    end

    table.sort(task_anchors, function(a, b)
        if a.y ~= b.y then
            return a.y < b.y
        end
        if a.anchor_score ~= b.anchor_score then
            return a.anchor_score < b.anchor_score
        end
        return a.x < b.x
    end)

    local task_buttons = {}
    for _, anchor in ipairs(task_anchors) do
        local merged = task_buttons[#task_buttons]
        if merged ~= nil
            and math.abs((tonumber(merged.y) or 0) - (tonumber(anchor.y) or 0)) <= 10
            and math.abs((tonumber(merged.x) or 0) - (tonumber(anchor.x) or 0)) <= 80
        then
            local replace = (tonumber(anchor.anchor_score) or math.huge) < (tonumber(merged.anchor_score) or math.huge)
                or (
                    (tonumber(anchor.anchor_score) or math.huge) == (tonumber(merged.anchor_score) or math.huge)
                    and tostring(anchor.kind or "") == "button"
                    and tostring(merged.kind or "") ~= "button"
                )
            if replace then
                merged.kind = anchor.kind
                merged.x = anchor.x
                merged.y = anchor.y
                merged.item = anchor.item
                merged.anchor_score = anchor.anchor_score
            end
        else
            task_buttons[#task_buttons + 1] = {
                kind = anchor.kind,
                x = anchor.x,
                y = anchor.y,
                item = anchor.item,
                anchor_score = anchor.anchor_score
            }
        end
    end

    local tasks = {}
    local debug_candidates = {}
    local seen = {}

    local function prefer_task_entry(candidate, existing)
        if type(candidate) ~= "table" then
            return false
        end
        if type(existing) ~= "table" then
            return true
        end

        local candidate_is_button = tostring(candidate.button_kind or "") == "button"
        local existing_is_button = tostring(existing.button_kind or "") == "button"
        if candidate_is_button ~= existing_is_button then
            return candidate_is_button
        end

        local candidate_anchor_score = tonumber(candidate.button_anchor_score) or math.huge
        local existing_anchor_score = tonumber(existing.button_anchor_score) or math.huge
        if candidate_anchor_score ~= existing_anchor_score then
            return candidate_anchor_score < existing_anchor_score
        end

        local candidate_title_score = tonumber(candidate.button_title_score) or -math.huge
        local existing_title_score = tonumber(existing.button_title_score) or -math.huge
        if candidate_title_score ~= existing_title_score then
            return candidate_title_score > existing_title_score
        end

        local candidate_has_addr = tonumber(candidate.button_addr) ~= nil and tonumber(candidate.button_addr) ~= 0
        local existing_has_addr = tonumber(existing.button_addr) ~= nil and tonumber(existing.button_addr) ~= 0
        if candidate_has_addr ~= existing_has_addr then
            return candidate_has_addr
        end

        local candidate_button_x = tonumber(candidate.button_x) or 0
        local existing_button_x = tonumber(existing.button_x) or 0
        if candidate_button_x ~= existing_button_x then
            return candidate_button_x > existing_button_x
        end

        return false
    end

    for button_index, button in ipairs(task_buttons) do
        local best = nil
        local best_score = nil
        for _, item in ipairs(snapshot.texts or {}) do
            local text_x = as_number(item.x)
            local text_y = as_number(item.y)
            if text_x ~= nil and text_y ~= nil then
                local raw_text = trim_text(item.text)
                local title, kind = normalize_task_title(raw_text)
                local dx = text_x - button.x
                local dy = math.abs(text_y - button.y)
                local in_band = dx >= -40 and dx <= 460 and dy <= 54
                if in_band and #debug_candidates < 10 then
                    debug_candidates[#debug_candidates + 1] = string.format(
                        "btn_y=%.1f text=%s dx=%.1f dy=%.1f",
                        tonumber(button.y) or 0,
                        trim_text(raw_text),
                        tonumber(dx) or 0,
                        tonumber(dy) or 0
                    )
                end
                if in_band and title ~= nil then
                    local score = 600 - dy * 8 - math.abs(dx - 88) * 1.6 - math.abs(#title - 10) * 0.8
                    if kind ~= "" then
                        score = score + 25
                    end
                    if best_score == nil or score > best_score then
                        best_score = score
                        best = {
                            title = title,
                            raw_text = raw_text,
                            kind = kind,
                            x = text_x,
                            y = text_y,
                            title_addr = as_number(item and item.addr) or (item and item.addr),
                            title_name = tostring(item and item.name or ""),
                            title_fullname = tostring(item and (item.Fullname or item.fullname) or ""),
                            button_kind = tostring(button.kind or ""),
                            button_anchor_score = tonumber(button.anchor_score) or 0,
                            button_title_score = score,
                            button_addr = as_number(button.item and button.item.addr) or (button.item and button.item.addr),
                            button_x = button.x,
                            button_y = button.y,
                            button_name = tostring(button.item and button.item.name or ""),
                            button_fullname = tostring(button.item and button.item.Fullname or button.item and button.item.fullname or "")
                        }
                    end
                end
            end
        end

        if best ~= nil then
            local detail_texts = {}
            local detail_debug_candidates = {}
            local detail_seen = {}
            local next_button = task_buttons[button_index + 1]
            local next_button_y = as_number(next_button and next_button.y)
            local next_task_boundary_y = next_button_y
            for _, item in ipairs(snapshot.texts or {}) do
                local text_y = as_number(item.y)
                local raw_text = trim_text(item.text)
                if text_y ~= nil
                    and text_y > button.y + 8
                    and classify_task_kind(raw_text) ~= ""
                then
                    if next_task_boundary_y == nil or text_y < next_task_boundary_y then
                        next_task_boundary_y = text_y
                    end
                end
            end
            for _, item in ipairs(snapshot.texts or {}) do
                local text_x = as_number(item.x)
                local text_y = as_number(item.y)
                if text_x ~= nil and text_y ~= nil then
                    local raw_text = trim_text(item.text)
                    local dx = text_x - button.x
                    local dy = text_y - button.y
                    local normalized_raw = normalize_text(raw_text)
                    local looks_like_task_header = classify_task_kind(raw_text) ~= ""
                    local looks_like_task_status = looks_like_task_status_text(raw_text)
                    local in_detail_band = dx >= -80 and dx <= 620 and dy >= 8 and dy <= 150
                    local before_next_task = next_task_boundary_y == nil or text_y < next_task_boundary_y - 6
                    local looks_numeric_only = raw_text:match("^[%d%s%/%-%:%+]+$") ~= nil
                    local accepted = in_detail_band
                        and before_next_task
                        and raw_text ~= ""
                        and normalized_raw ~= normalize_text(best.raw_text or "")
                        and not looks_numeric_only
                        and not looks_like_task_header
                        and not looks_like_task_status
                        and not detail_seen[normalized_raw]
                    local near_detail_probe = dx >= -160 and dx <= 720 and dy >= -30 and dy <= 220
                    if near_detail_probe and raw_text ~= "" and #detail_debug_candidates < 12 then
                        detail_debug_candidates[#detail_debug_candidates + 1] = string.format(
                            "text=%s dx=%.1f dy=%.1f boundary=%s before_next=%s header=%s status=%s numeric=%s accepted=%s",
                            raw_text,
                            tonumber(dx) or 0,
                            tonumber(dy) or 0,
                            next_task_boundary_y ~= nil and string.format("%.1f", next_task_boundary_y) or "nil",
                            before_next_task and "true" or "false",
                            looks_like_task_header and "true" or "false",
                            looks_like_task_status and "true" or "false",
                            looks_numeric_only and "true" or "false",
                            accepted and "true" or "false"
                        )
                    end
                    if accepted then
                        detail_seen[normalized_raw] = true
                        detail_texts[#detail_texts + 1] = {
                            text = raw_text,
                            x = text_x,
                            y = text_y
                        }
                    end
                end
            end

            table.sort(detail_texts, function(a, b)
                if a.y ~= b.y then
                    return a.y < b.y
                end
                return a.x < b.x
            end)

            local key = normalize_text(best.raw_text)
            if key ~= "" then
                best.detail_texts = detail_texts
                best.detail_debug_candidates = detail_debug_candidates
                if #detail_texts > 0 then
                    best.detail = detail_texts[1].text
                end

                local existing_index = seen[key]
                if type(existing_index) ~= "number" then
                    tasks[#tasks + 1] = best
                    seen[key] = #tasks
                elseif prefer_task_entry(best, tasks[existing_index]) then
                    tasks[existing_index] = best
                end
            end
        end
    end

    return {
        tasks = tasks,
        button_count = #task_buttons,
        debug_candidates = debug_candidates
    }, nil
end

function nav.find_task_panel_entry(query, snapshot, opts)
    opts = opts or {}
    local info, err = nav.get_task_panel_info(snapshot)
    if type(info) ~= "table" or type(info.tasks) ~= "table" then
        return nil, err or "task panel unavailable."
    end

    local function trim_text(value)
        return tostring(value or ""):gsub("^%s+", ""):gsub("%s+$", "")
    end

    local function normalize_text(value)
        local text = trim_text(value)
        text = text:gsub("^主线%s*", "")
        text = text:gsub("^支线%s*", "")
        text = text:gsub("^赛季%s*", "")
        return text:lower()
    end

    local query_text = normalize_text(query)
    if query_text == "" then
        return nil, "task panel query is empty."
    end

    local exact = opts.exact == true
    local best = nil
    local best_score = nil

    for index, item in ipairs(info.tasks or {}) do
        local haystack = {
            normalize_text(item.raw_text),
            normalize_text(item.title),
            normalize_text(item.kind),
            normalize_text(item.detail)
        }
        local matched = false
        local score = 0
        for _, value in ipairs(haystack) do
            if value ~= "" then
                if exact then
                    if value == query_text then
                        matched = true
                        score = 1000 - index
                        break
                    end
                else
                    if value:find(query_text, 1, true) then
                        matched = true
                        score = 800 - math.abs(#value - #query_text) - index
                        break
                    end
                end
            end
        end
        if matched and (best_score == nil or score > best_score) then
            best = item
            best_score = score
        end
    end

    if best == nil then
        return nil, "task panel entry not found."
    end

    return best, nil
end

function nav.click_task_panel_entry(query, snapshot, opts)
    local item, err = nav.find_task_panel_entry(query, snapshot, opts)
    if type(item) ~= "table" then
        return false, err
    end

    local addr = as_number(item.button_addr)
    if addr == nil or addr == 0 then
        return false, "task panel entry button address unavailable."
    end

    local ok, click_err = nav.control_click(addr)
    if not ok then
        return false, click_err
    end

    return true, item
end

local MAP_UI_TEXT_TARGETS

function nav.get_map_ui_info(snapshot)
    if type(snapshot) ~= "table" then
        local ui, err = nav.enum_ui()
        if type(ui) ~= "table" then
            return nil, err or "enum_ui failed."
        end
        snapshot = ui
    end

    local function normalize_text(value)
        return tostring(value or ""):gsub("^%s+", ""):gsub("%s+$", ""):lower()
    end

    local function make_text_info(item)
        return {
            kind = "text",
            addr = as_number(item.addr) or item.addr,
            name = tostring(item.name or ""),
            text = tostring(item.text or ""):gsub("^%s+", ""):gsub("%s+$", ""),
            fullname = tostring(item.Fullname or item.fullname or ""),
            x = as_number(item.x),
            y = as_number(item.y),
            item = item
        }
    end

    local function contains_all(value, tokens)
        local text = normalize_text(value)
        if text == "" then
            return false
        end
        for _, token in ipairs(tokens or {}) do
            local normalized = normalize_text(token)
            if normalized ~= "" and not text:find(normalized, 1, true) then
                return false
            end
        end
        return true
    end

    local function contains_any(value, tokens)
        local text = normalize_text(value)
        if text == "" then
            return false
        end
        for _, token in ipairs(tokens or {}) do
            local normalized = normalize_text(token)
            if normalized ~= "" and text:find(normalized, 1, true) then
                return true
            end
        end
        return false
    end

    local function trim_dump_text(value, max_len)
        local text = tostring(value or ""):gsub("[\r\n\t]", " ")
        local limit = math.max(1, tonumber(max_len) or 48)
        if #text > limit then
            text = text:sub(1, limit - 3) .. "..."
        end
        return text
    end

    local function looks_numeric_text(value)
        local text = tostring(value or ""):gsub("[%s%p]", "")
        if text == "" then
            return false
        end
        return text:match("^%d+$") ~= nil
    end

    local function find_target_text(spec)
        for _, item in ipairs(snapshot.texts or {}) do
            local fullname = normalize_text(item.Fullname or item.fullname or "")
            local name = normalize_text(item.name)
            for _, expected in ipairs(spec and spec.fullnames or {}) do
                if fullname == normalize_text(expected) then
                    local info = make_text_info(item)
                    if info.text ~= "" then
                        return info
                    end
                end
            end
            if contains_all(fullname, spec and spec.tokens)
                or contains_all(name, spec and spec.tokens)
                or contains_any(item.text, spec and spec.text_patterns)
            then
                local info = make_text_info(item)
                if info.text ~= "" then
                    return info
                end
            end
        end
        return nil
    end

    local current_map = find_target_text(MAP_UI_TEXT_TARGETS.current_map)
    local monster_level = find_target_text(MAP_UI_TEXT_TARGETS.monster_level)
    local remaining_enemies = find_target_text(MAP_UI_TEXT_TARGETS.remaining_enemies)
    local panel_debug_candidates = nil

    local function collect_map_debug_candidates()
        local max_x = 0
        local max_y = 0
        for _, item in ipairs(snapshot.texts or {}) do
            local x = as_number(item.x)
            local y = as_number(item.y)
            if x ~= nil and x > max_x then
                max_x = x
            end
            if y ~= nil and y > max_y then
                max_y = y
            end
        end

        local client_w = nil
        local client_h = nil
        local hwnd = nil
        if type(nav.window_hwnd) == "function" and type(wnd) == "table" and type(wnd.client_rect) == "function" then
            hwnd = nav.window_hwnd()
            if hwnd then
                local _, _, w, h = wnd.client_rect(hwnd)
                client_w = as_number(w)
                client_h = as_number(h)
            end
        end

        local ref_w = client_w or max_x
        local ref_h = client_h or max_y
        local right_x = nil
        local top_y = nil
        if ref_w ~= nil and ref_w > 0 then
            right_x = ref_w * 0.52
        end
        if ref_h ~= nil and ref_h > 0 then
            top_y = ref_h * 0.48
        end

        local candidates = {}
        for _, item in ipairs(snapshot.texts or {}) do
            local info = make_text_info(item)
            local full = normalize_text(info.fullname)
            local name = normalize_text(info.name)
            local text = normalize_text(info.text)
            local x = as_number(info.x)
            local y = as_number(info.y)
            local in_top_right = x ~= nil
                and y ~= nil
                and (right_x == nil or x >= right_x)
                and (top_y == nil or y <= top_y)
            local fullname_hit = contains_any(full, {
                "minimap",
                "worldlevel",
                "fightmapdetailview",
                "monsterlevel",
                "monsternumber",
                "mapdetail",
                "levelname"
            })
            local name_hit = contains_any(name, {
                "minimap",
                "worldlevel",
                "fightmapdetailview",
                "monsterlevel",
                "monsternumber",
                "mapdetail",
                "levelname"
            })
            local text_keyword_hit = contains_any(text, {
                "怪物等级",
                "地图内剩余敌人数量",
                "剩余敌人",
                "敌人数量",
                "世界等级"
            })
            local blocked_map_text = contains_any(text, {
                "怪物等级",
                "地图内剩余敌人数量",
                "剩余敌人",
                "敌人数量",
                "任务",
                "生命",
                "法力",
                "护盾",
                "经验",
                "金币",
                "世界等级",
                "按键",
                "对话"
            })
            local plausible_map_text = in_top_right
                and text ~= ""
                and not blocked_map_text
                and not looks_numeric_text(info.text)

            text_keyword_hit = contains_any(text, {
                "\u{602A}\u{7269}\u{7B49}\u{7EA7}",
                "\u{5730}\u{56FE}\u{5185}\u{5269}\u{4F59}\u{654C}\u{4EBA}\u{6570}\u{91CF}",
                "\u{5269}\u{4F59}\u{654C}\u{4EBA}",
                "\u{654C}\u{4EBA}\u{6570}\u{91CF}",
                "\u{4E16}\u{754C}\u{7B49}\u{7EA7}"
            })
            blocked_map_text = contains_any(text, {
                "\u{602A}\u{7269}\u{7B49}\u{7EA7}",
                "\u{5730}\u{56FE}\u{5185}\u{5269}\u{4F59}\u{654C}\u{4EBA}\u{6570}\u{91CF}",
                "\u{5269}\u{4F59}\u{654C}\u{4EBA}",
                "\u{654C}\u{4EBA}\u{6570}\u{91CF}",
                "\u{4EFB}\u{52A1}",
                "\u{751F}\u{547D}",
                "\u{6CD5}\u{529B}",
                "\u{62A4}\u{76FE}",
                "\u{7ECF}\u{9A8C}",
                "\u{91D1}\u{5E01}",
                "\u{4E16}\u{754C}\u{7B49}\u{7EA7}",
                "\u{6309}\u{952E}",
                "\u{5BF9}\u{8BDD}"
            })
            plausible_map_text = in_top_right
                and text ~= ""
                and not blocked_map_text
                and not looks_numeric_text(info.text)

            if fullname_hit or name_hit or text_keyword_hit or plausible_map_text then
                local score = 0
                if fullname_hit then
                    score = score + 100
                end
                if name_hit then
                    score = score + 80
                end
                if text_keyword_hit then
                    score = score + 60
                end
                if plausible_map_text then
                    score = score + 40
                end
                candidates[#candidates + 1] = {
                    info = info,
                    score = score,
                    in_top_right = in_top_right,
                    plausible_map_text = plausible_map_text
                }
            end
        end

        table.sort(candidates, function(a, b)
            if a.score ~= b.score then
                return a.score > b.score
            end
            local ay = as_number(a.info.y) or math.huge
            local by = as_number(b.info.y) or math.huge
            if ay ~= by then
                return ay < by
            end
            local ax = as_number(a.info.x) or 0
            local bx = as_number(b.info.x) or 0
            if ax ~= bx then
                return ax > bx
            end
            return tostring(a.info.fullname or "") < tostring(b.info.fullname or "")
        end)

        local debug_lines = {}
        for index, candidate in ipairs(candidates) do
            if index > 8 then
                break
            end
            local info = candidate.info
            debug_lines[#debug_lines + 1] = string.format(
                "text=%s name=%s fullname=%s x=%s y=%s top_right=%s plausible_map=%s",
                trim_dump_text(info.text, 36),
                trim_dump_text(info.name, 42),
                trim_dump_text(info.fullname, 78),
                tostring(info.x or ""),
                tostring(info.y or ""),
                tostring(candidate.in_top_right),
                tostring(candidate.plausible_map_text)
            )
        end

        return candidates, debug_lines
    end

    local function collect_map_panel_candidates(anchor)
        if type(anchor) ~= "table" then
            return {}, {}
        end

        local anchor_x = as_number(anchor.x)
        local anchor_y = as_number(anchor.y)
        if anchor_x == nil or anchor_y == nil then
            return {}, {}
        end

        local blocked_tokens = {
            "\u{602A}\u{7269}\u{7B49}\u{7EA7}",
            "\u{5730}\u{56FE}\u{5185}\u{5269}\u{4F59}\u{654C}\u{4EBA}\u{6570}\u{91CF}",
            "\u{5269}\u{4F59}\u{654C}\u{4EBA}",
            "\u{654C}\u{4EBA}\u{6570}\u{91CF}",
            "\u{4E16}\u{754C}\u{7B49}\u{7EA7}",
            "\u{4EFB}\u{52A1}",
            "\u{751F}\u{547D}",
            "\u{6CD5}\u{529B}",
            "\u{62A4}\u{76FE}",
            "\u{7ECF}\u{9A8C}",
            "\u{91D1}\u{5E01}",
            "\u{6309}\u{952E}",
            "\u{5BF9}\u{8BDD}",
            "fps",
            "ms",
            "lv",
            "level"
        }

        local candidates = {}
        local seen_keys = {}
        for _, item in ipairs(snapshot.texts or {}) do
            local info = make_text_info(item)
            local x = as_number(info.x)
            local y = as_number(info.y)
            if x ~= nil and y ~= nil and info.text ~= "" then
                local dx = math.abs(x - anchor_x)
                local dy = y - anchor_y
                local abs_dy = math.abs(dy)
                local normalized_text = normalize_text(info.text)
                local near_panel = dx <= 260 and dy >= -120 and dy <= 70
                local blocked = contains_any(normalized_text, blocked_tokens)
                    or looks_numeric_text(info.text)
                    or normalized_text == normalize_text(anchor.text)
                if near_panel and not blocked then
                    local dedupe_key = table.concat({
                        normalized_text,
                        string.format("%.1f", x),
                        string.format("%.1f", y)
                    }, "|")
                    if seen_keys[dedupe_key] then
                        goto continue_panel_candidate
                    end
                    seen_keys[dedupe_key] = true

                    local score = 0
                    if dy <= 0 then
                        score = score + 120
                    end
                    if abs_dy <= 80 then
                        score = score + 80
                    end
                    score = score + math.max(0, 220 - dx)
                    if #tostring(info.text or "") <= 18 then
                        score = score + 20
                    end

                    candidates[#candidates + 1] = {
                        info = info,
                        score = score,
                        dx = dx,
                        dy = dy
                    }
                end
            end

            ::continue_panel_candidate::
        end

        table.sort(candidates, function(a, b)
            if a.score ~= b.score then
                return a.score > b.score
            end
            local ady = math.abs(a.dy or 0)
            local bdy = math.abs(b.dy or 0)
            if ady ~= bdy then
                return ady < bdy
            end
            if a.dx ~= b.dx then
                return a.dx < b.dx
            end
            return tostring(a.info.text or "") < tostring(b.info.text or "")
        end)

        local debug_lines = {}
        for index, candidate in ipairs(candidates) do
            if index > 6 then
                break
            end
            local info = candidate.info
            debug_lines[#debug_lines + 1] = string.format(
                "text=%s x=%s y=%s dx=%.1f dy=%.1f score=%.1f name=%s fullname=%s",
                trim_dump_text(info.text, 36),
                tostring(info.x or ""),
                tostring(info.y or ""),
                tonumber(candidate.dx) or 0,
                tonumber(candidate.dy) or 0,
                tonumber(candidate.score) or 0,
                trim_dump_text(info.name, 42),
                trim_dump_text(info.fullname, 78)
            )
        end

        return candidates, debug_lines
    end

    if current_map == nil then
        if type(monster_level) == "table" then
            local panel_candidates, panel_debug = collect_map_panel_candidates(monster_level)
            if #panel_debug > 0 then
                panel_debug_candidates = panel_debug
            end
            if #panel_candidates == 1 then
                current_map = panel_candidates[1].info
            elseif #panel_candidates >= 2 then
                local best = panel_candidates[1]
                local second = panel_candidates[2]
                if (tonumber(best.score) or 0) >= ((tonumber(second.score) or 0) + 80) then
                    current_map = best.info
                end
            end
        end

        local debug_candidates = collect_map_debug_candidates()
        local plausible_count = 0
        local plausible_match = nil
        for _, candidate in ipairs(debug_candidates) do
            if candidate.plausible_map_text then
                plausible_count = plausible_count + 1
                if plausible_match == nil then
                    plausible_match = candidate.info
                end
            end
        end
        if plausible_count == 1 and plausible_match ~= nil then
            current_map = plausible_match
        end
    end

    if current_map or monster_level or remaining_enemies then
        local debug_candidates = nil
        if current_map == nil then
            if type(panel_debug_candidates) == "table" and #panel_debug_candidates > 0 then
                debug_candidates = panel_debug_candidates
            else
                local _, debug_lines = collect_map_debug_candidates()
                if #debug_lines > 0 then
                    debug_candidates = debug_lines
                end
            end
        end
        return {
            current_map = current_map,
            monster_level = monster_level,
            remaining_enemies = remaining_enemies,
            debug_candidates = debug_candidates
        }, nil
    end

    local debug_candidates, debug_lines = collect_map_debug_candidates()

    if #debug_lines > 0 then
        return nil, string.format(
            "map ui texts not found. texts=%d candidates: %s",
            #(snapshot.texts or {}),
            table.concat(debug_lines, " | ")
        )
    end

    return nil, string.format(
        "map ui texts not found. texts=%d candidates=0",
        #(snapshot.texts or {})
    )
end

function nav.get_current_map_name(snapshot)
    local info, err = nav.get_map_ui_info(snapshot)
    if type(info) ~= "table" or type(info.current_map) ~= "table" then
        return nil, err or "current map text not found."
    end

    return tostring(info.current_map.text or ""), info.current_map
end

function nav.enum_ground_items()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    if type(nav.game_api.EnumGroundItem) ~= "function" then
        return nil, "EnumGroundItem is not available."
    end

    local items, enum_err = quiet_call(nav.game_api.EnumGroundItem)
    if items == nil then
        return nil, enum_err or "EnumGroundItem failed."
    end

    return safe_items(items)
end

function nav.enum_portals()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    if type(nav.game_api.EnumPortal) ~= "function" then
        return nil, "EnumPortal is not available."
    end

    local items, enum_err = quiet_call(nav.game_api.EnumPortal)
    if items == nil then
        return nil, enum_err or "EnumPortal failed."
    end

    return safe_items(items)
end

function nav.enum_npcs()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    if type(nav.game_api.EnumNPC) ~= "function" then
        return nil, "EnumNPC is not available."
    end

    local items, enum_err = quiet_call(nav.game_api.EnumNPC)
    if items == nil then
        return nil, enum_err or "EnumNPC failed."
    end

    return safe_items(items)
end

function nav.enum_monsters()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    if type(nav.game_api.EnumMonster) ~= "function" then
        return nil, "EnumMonster is not available."
    end

    local items, enum_err = quiet_call(nav.game_api.EnumMonster)
    if items == nil then
        return nil, enum_err or "EnumMonster failed."
    end

    return safe_items(items)
end

function nav.control_click(addr)
    local ok, err = ensure_api()
    if not ok then
        return false, err
    end

    if type(addr) ~= "number" or addr == 0 then
        return false, "Invalid control address."
    end

    if type(nav.game_api.control_click) ~= "function" then
        return false, "control_click is not available."
    end

    if type(human_mouse) == "table" and type(human_mouse.sleep_random) == "function" then
        human_mouse.sleep_random(400, 2000)
    end

    local click_ok, click_err = quiet_call(nav.game_api.control_click, addr)
    if click_ok == nil or click_ok == false then
        return false, click_err or "control_click failed."
    end

    return true
end

local function ui_button_score(item)
    local x = as_number(item.x) or 0
    local y = as_number(item.y) or 0
    return y * 100000 - x
end

local function lower_text(value)
    return tostring(value or ""):lower()
end

local function match_any(value, patterns)
    local text = lower_text(value)
    for _, pattern in ipairs(patterns or {}) do
        if pattern ~= "" and text:find(pattern:lower(), 1, true) then
            return true
        end
    end
    return false
end

local function normalize_exact_match_text(value)
    return tostring(value or ""):gsub("^%s+", ""):gsub("%s+$", ""):lower()
end

local function match_exact_any(value, patterns)
    local text = normalize_exact_match_text(value)
    if text == "" then
        return false
    end

    for _, pattern in ipairs(patterns or {}) do
        if normalize_exact_match_text(pattern) == text then
            return true
        end
    end

    return false
end

local function item_match_text(item)
    return table.concat({
        tostring(item.name or ""),
        tostring(item.text or ""),
        tostring(item.Fullname or "")
    }, " ")
end

MAP_UI_TEXT_TARGETS = {
    current_map = {
        fullnames = {
            "UITextBlock Transient.GameEngine.CoreGameInstance.MiniMap_C.WidgetTree.worldLevelName"
        },
        tokens = {
            "minimap_c",
            "worldlevelname"
        },
        text_patterns = {
        }
    },
    monster_level = {
        fullnames = {
            "UITextBlock Transient.GameEngine.CoreGameInstance.FightMapDetailView_C.WidgetTree.monsterLevel"
        },
        tokens = {
            "fightmapdetailview_c",
            "monsterlevel"
        },
        text_patterns = {
            "怪物等级",
            "monster level"
        }
    },
    remaining_enemies = {
        fullnames = {
            "UITextBlock Transient.GameEngine.CoreGameInstance.FightMapDetailView_C.WidgetTree.MonsterNumber"
        },
        tokens = {
            "fightmapdetailview_c",
            "monsternumber"
        },
        text_patterns = {
            "地图内剩余敌人数量",
            "剩余敌人",
            "敌人数量",
            "remaining enemies"
        }
    }
}

MAP_UI_TEXT_TARGETS.current_map.text_patterns = {}
MAP_UI_TEXT_TARGETS.monster_level.text_patterns = {
    "\u{602A}\u{7269}\u{7B49}\u{7EA7}",
    "monster level"
}
MAP_UI_TEXT_TARGETS.remaining_enemies.text_patterns = {
    "\u{5730}\u{56FE}\u{5185}\u{5269}\u{4F59}\u{654C}\u{4EBA}\u{6570}\u{91CF}",
    "\u{5269}\u{4F59}\u{654C}\u{4EBA}",
    "\u{654C}\u{4EBA}\u{6570}\u{91CF}",
    "remaining enemies"
}

local function control_priority(kind)
    if kind == "button" then
        return 0
    end
    if kind == "image" then
        return 10
    end
    if kind == "text" then
        return 20
    end
    return 30
end

local function control_fullname(item)
    return tostring(item.Fullname or item.fullname or "")
end

local function make_control_info(kind, item)
    return {
        kind = kind,
        addr = as_number(item.addr) or item.addr,
        name = tostring(item.name or ""),
        text = tostring(item.text or ""),
        fullname = control_fullname(item),
        x = as_number(item.x),
        y = as_number(item.y),
        item = item
    }
end

local format_control_dump_fields
local control_dump_info
local format_control_item_dump

local function control_dump_less(a, b)
    local ay = as_number(a.y) or 0
    local by = as_number(b.y) or 0
    if ay ~= by then
        return ay < by
    end

    local ax = as_number(a.x) or 0
    local bx = as_number(b.x) or 0
    if ax ~= bx then
        return ax < bx
    end

    local ap = control_priority(a.kind)
    local bp = control_priority(b.kind)
    if ap ~= bp then
        return ap < bp
    end

    return tostring(a.addr or "") < tostring(b.addr or "")
end

local function collect_controls(snapshot, opts)
    opts = opts or {}

    local include = opts.include_patterns or {}
    local exclude = opts.exclude_patterns or {}
    local exact_texts = opts.exact_texts or {}
    local controls = {}

    local function push(kind, items)
        for _, item in ipairs(items or {}) do
            local info = make_control_info(kind, item)
            if info.x ~= nil and info.y ~= nil then
                local text = table.concat({
                    info.name,
                    info.text,
                    info.fullname
                }, " ")
                if (#include == 0 or match_any(text, include))
                    and (#exact_texts == 0 or match_exact_any(info.text, exact_texts))
                    and not match_any(text, exclude)
                then
                    controls[#controls + 1] = info
                end
            end
        end
    end

    if opts.include_buttons ~= false then
        push("button", snapshot.buttons)
    end
    if opts.include_images ~= false then
        push("image", snapshot.images)
    end
    if opts.include_texts == true then
        push("text", snapshot.texts)
    end

    return controls
end

function nav.list_visible_controls(opts)
    opts = opts or {}

    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return nil, err
    end

    local collect_opts = {}
    for key, value in pairs(opts) do
        collect_opts[key] = value
    end

    if collect_opts.include_buttons == nil then
        collect_opts.include_buttons = true
    end
    if collect_opts.include_texts == nil then
        collect_opts.include_texts = true
    end
    if collect_opts.include_images == nil then
        collect_opts.include_images = false
    end

    local controls = collect_controls(snapshot, collect_opts)
    table.sort(controls, control_dump_less)

    local limit = collect_opts.limit
    if type(limit) == "number" and limit > 0 and #controls > limit then
        local clipped = {}
        for index = 1, limit do
            clipped[index] = controls[index]
        end
        controls = clipped
    end

    return controls, snapshot
end

local function control_region(opts)
    local width = opts.width
    local height = opts.height

    if nav.pid and (not width or not height) and type(wnd) == "table" and type(wnd.find_by_pid) == "function" then
        local hwnd = wnd.find_by_pid(nav.pid)
        if hwnd and type(wnd.client_rect) == "function" then
            local _, _, w, h = wnd.client_rect(hwnd)
            width = width or w
            height = height or h
        end
    end

    local max_x = opts.max_x
    local min_y = opts.min_y

    if not max_x and width then
        max_x = width * (opts.max_x_ratio or 0.18)
    end

    if not min_y and height then
        min_y = height * (opts.min_y_ratio or 0.72)
    end

    return max_x, min_y
end

local function item_in_region(item, max_x, min_y)
    local x = as_number(item.x)
    local y = as_number(item.y)
    if x == nil or y == nil then
        return false
    end

    if max_x and x > max_x then
        return false
    end
    if min_y and y < min_y then
        return false
    end

    return true
end

local function default_exclude_patterns()
    return {
        "fightchatview",
        "chatsubwindowitem",
        "subwindowupbtn",
        "subwindowdownbtn",
        "chat"
    }
end

local function make_candidate(kind, item)
    local x = as_number(item.x) or 0
    local y = as_number(item.y) or 0
    local score = y * 100000 - x
    if kind == "image" then
        score = score + 1000000000
    end

    return {
        kind = kind,
        item = item,
        score = score
    }
end

function nav.find_bottom_left_candidates(opts)
    opts = opts or {}

    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return nil, err
    end

    local max_x, min_y = control_region(opts)
    local exclude = opts.exclude_patterns or default_exclude_patterns()
    local candidates = {}

    for _, item in ipairs(snapshot.images or {}) do
        if item_in_region(item, max_x, min_y) then
            local name = table.concat({
                tostring(item.Fullname or ""),
                tostring(item.name or ""),
                tostring(item.text or "")
            }, " ")
            if not match_any(name, exclude) then
                candidates[#candidates + 1] = make_candidate("image", item)
            end
        end
    end

    for _, item in ipairs(snapshot.buttons or {}) do
        if item_in_region(item, max_x, min_y) then
            local name = table.concat({
                tostring(item.name or ""),
                tostring(item.text or "")
            }, " ")
            if not match_any(name, exclude) then
                candidates[#candidates + 1] = make_candidate("button", item)
            end
        end
    end

    table.sort(candidates, function(a, b)
        return a.score > b.score
    end)

    return candidates
end

function nav.find_button_by_match(opts)
    opts = opts or {}

    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return nil, err
    end

    local include = opts.include_patterns or {}
    local exclude = opts.exclude_patterns or {}
    local buttons = snapshot.buttons or {}
    local best = nil

    for _, item in ipairs(buttons) do
        local text = item_match_text(item)
        if (#include == 0 or match_any(text, include)) and not match_any(text, exclude) then
            local score = ui_button_score(item)
            if not best or score > best.score then
                best = { item = item, score = score }
            end
        end
    end

    if not best then
        return nil, "Matched button not found."
    end

    return best.item
end

function nav.find_button_near_point(target_x, target_y, opts)
    opts = opts or {}

    local snapshot = opts.snapshot
    if type(snapshot) ~= "table" then
        local err = nil
        snapshot, err = nav.enum_ui()
        if not snapshot then
            return nil, err
        end
    end

    local include = opts.include_patterns or {}
    local exclude = opts.exclude_patterns or {}
    local max_distance = opts.max_distance
    local best = nil

    for _, item in ipairs(snapshot.buttons or {}) do
        local text = item_match_text(item)
        if (#include == 0 or match_any(text, include)) and not match_any(text, exclude) then
            local x = as_number(item.x)
            local y = as_number(item.y)
            if x ~= nil and y ~= nil then
                local distance = distance_2d(target_x, target_y, x, y)
                if type(max_distance) ~= "number" or distance <= max_distance then
                    local score = distance + control_priority("button")
                    if not best or score < best.score then
                        best = {
                            kind = "button",
                            addr = item.addr,
                            name = tostring(item.name or ""),
                            text = tostring(item.text or ""),
                            fullname = tostring(item.Fullname or ""),
                            x = x,
                            y = y,
                            distance = distance,
                            score = score
                        }
                    end
                end
            end
        end
    end

    if not best then
        return nil, "Nearby button not found."
    end

    return best
end

function nav.find_button_by_locator(locator, opts)
    opts = opts or {}
    if type(locator) ~= "table" then
        return nil, "button locator is invalid."
    end

    local snapshot = opts.snapshot
    if type(snapshot) ~= "table" then
        local ui, err = nav.enum_ui()
        if type(ui) ~= "table" then
            return nil, err or "enum_ui failed."
        end
        snapshot = ui
    end

    local identity = lower_text(locator.fullname or locator.name or locator.button_fullname or "")
    local include = opts.include_patterns or locator.include_patterns or {}
    if #include == 0 then
        if identity:find("taskitem_c.widgettree.taskbtn", 1, true) ~= nil then
            include = { "taskitem_c.widgettree.taskbtn" }
        elseif identity ~= "" then
            include = { identity }
        end
    end

    local hint_x = as_number(locator.x or locator.hint_x or locator.hint_client_x)
    local hint_y = as_number(locator.y or locator.hint_y or locator.hint_client_y)
    local max_distance = as_number(opts.max_distance or locator.max_distance or locator.hint_max_distance) or 140
    local related_text = tostring(locator.related_text or locator.rel1_text or "")
    local related_dx = as_number(locator.related_dx or locator.rel1_dx)
    local related_dy = as_number(locator.related_dy or locator.rel1_dy)
    local related_tolerance = as_number(opts.related_tolerance or locator.related_tolerance) or 36
    local distance_anchor_text = normalize_exact_match_text(
        locator.distance_anchor_exact_text or locator.anchor_exact_text or ""
    )
    local distance_button_name = tostring(locator.distance_button_name or "")
    local distance_min = as_number(locator.distance_min)
    local distance_max = as_number(locator.distance_max)
    if distance_min ~= nil and distance_max ~= nil and distance_max < distance_min then
        distance_min, distance_max = distance_max, distance_min
    end
    local distance_target = nil
    if distance_min ~= nil and distance_max ~= nil then
        distance_target = (distance_min + distance_max) * 0.5
    else
        distance_target = distance_min or distance_max
    end

    local best = nil
    local related_miss = 0
    local distance_anchor_miss = 0
    local name_miss = 0
    for _, item in ipairs(snapshot.buttons or {}) do
        local text = item_match_text(item)
        if (#include == 0 or match_any(text, include)) and not match_any(text, opts.exclude_patterns or {}) then
            local x = as_number(item.x)
            local y = as_number(item.y)
            if x ~= nil and y ~= nil and item.addr ~= nil then
                local item_name = tostring(item.name or "")
                local item_fullname = tostring(item.Fullname or item.fullname or "")
                local distance_name_hit = distance_button_name == ""
                    or item_name == distance_button_name
                    or item_fullname == distance_button_name
                    or lower_text(item_fullname):find(lower_text(distance_button_name), 1, true) ~= nil
                if not distance_name_hit then
                    name_miss = name_miss + 1
                    goto continue_locator_button
                end

                local distance = hint_x ~= nil and hint_y ~= nil and distance_2d(hint_x, hint_y, x, y) or 0
                if hint_x == nil or hint_y == nil or distance <= max_distance then
                    local distance_anchor_hit = distance_anchor_text == ""
                    local distance_anchor_value = ""
                    local distance_anchor_score = 0
                    local distance_anchor_distance = nil
                    if distance_anchor_text ~= "" then
                        local best_anchor = nil
                        for _, text_item in ipairs(snapshot.texts or {}) do
                            local text_value = tostring(text_item.text or "")
                            if normalize_exact_match_text(text_value) == distance_anchor_text then
                                local text_x = as_number(text_item.x)
                                local text_y = as_number(text_item.y)
                                if text_x ~= nil and text_y ~= nil then
                                    local anchor_distance = distance_2d(x, y, text_x, text_y)
                                    local in_range = true
                                    if distance_min ~= nil and anchor_distance < distance_min then
                                        in_range = false
                                    end
                                    if distance_max ~= nil and anchor_distance > distance_max then
                                        in_range = false
                                    end
                                    if in_range then
                                        local anchor_score = distance_target ~= nil
                                            and math.abs(anchor_distance - distance_target)
                                            or 0
                                        if best_anchor == nil
                                            or anchor_score < best_anchor.score
                                            or (
                                                anchor_score == best_anchor.score
                                                and anchor_distance < (best_anchor.distance or math.huge)
                                            )
                                        then
                                            best_anchor = {
                                                text = text_value,
                                                score = anchor_score,
                                                distance = anchor_distance
                                            }
                                        end
                                    end
                                end
                            end
                        end

                        if best_anchor ~= nil then
                            distance_anchor_hit = true
                            distance_anchor_value = best_anchor.text
                            distance_anchor_score = best_anchor.score
                            distance_anchor_distance = best_anchor.distance
                        else
                            distance_anchor_miss = distance_anchor_miss + 1
                        end
                    end

                    local related_hit = related_text == ""
                    local related_hit_text = ""
                    local related_score = 0
                    if related_text ~= "" then
                        for _, text_item in ipairs(snapshot.texts or {}) do
                            local text_value = tostring(text_item.text or "")
                            if text_value == related_text or text_value:find(related_text, 1, true) then
                                local text_x = as_number(text_item.x)
                                local text_y = as_number(text_item.y)
                                if text_x ~= nil and text_y ~= nil then
                                    local dx = text_x - x
                                    local dy = text_y - y
                                    local gap = 0
                                    if related_dx ~= nil then
                                        gap = gap + math.abs(dx - related_dx)
                                    end
                                    if related_dy ~= nil then
                                        gap = gap + math.abs(dy - related_dy)
                                    end
                                    if (related_dx == nil and related_dy == nil) or gap <= related_tolerance then
                                        related_hit = true
                                        related_hit_text = text_value
                                        related_score = gap
                                        break
                                    end
                                end
                            end
                        end
                    end

                    if distance_anchor_hit and related_hit then
                        local score = distance + related_score + distance_anchor_score
                        if not best or score < best.score then
                            best = {
                                kind = "button",
                                addr = item.addr,
                                name = item_name,
                                text = tostring(item.text or ""),
                                fullname = item_fullname,
                                x = x,
                                y = y,
                                distance = distance,
                                score = score,
                                related_text = related_hit_text ~= "" and related_hit_text or distance_anchor_value,
                                related_distance = distance_anchor_distance
                            }
                        end
                    elseif not related_hit then
                        related_miss = related_miss + 1
                    end
                end
            end
        end
        ::continue_locator_button::
    end

    if not best then
        return nil, string.format(
            "locator matched no current button. include=%s hint=(%s,%s) max_distance=%.1f related=%s related_miss=%d anchor_text=%s anchor_miss=%d distance_range=(%s,%s) name=%s name_miss=%d",
            #include > 0 and table.concat(include, "|") or "",
            tostring(hint_x),
            tostring(hint_y),
            max_distance,
            related_text,
            related_miss,
            distance_anchor_text,
            distance_anchor_miss,
            tostring(distance_min),
            tostring(distance_max),
            distance_button_name,
            name_miss
        )
    end

    return best
end

function nav.window_hwnd()
    if not nav.pid then
        return nil, "Torch process is not initialized."
    end

    if type(wnd) ~= "table" or type(wnd.find_by_pid) ~= "function" then
        return nil, "wnd.find_by_pid is not available."
    end

    local hwnd = wnd.find_by_pid(nav.pid)
    if not hwnd then
        return nil, "Game window not found."
    end

    return hwnd
end

function nav.set_move_call_mouse_sync(opts)
    opts = opts or {}

    local cfg = nav.move_call_mouse_sync
    if type(opts.enabled) == "boolean" then
        cfg.enabled = opts.enabled
    end
    if type(opts.mode) == "string" and opts.mode ~= "" then
        cfg.mode = opts.mode
    end

    for _, key in ipairs({
        "radius",
        "near_target_distance",
        "target_change_distance",
        "padding_left",
        "padding_right",
        "padding_top",
        "padding_bottom"
    }) do
        local value = as_number(opts[key])
        if value ~= nil then
            cfg[key] = value
        end
    end
    if type(opts.swap_axes) == "boolean" then
        cfg.swap_axes = opts.swap_axes
    end
    if type(opts.invert_x) == "boolean" then
        cfg.invert_x = opts.invert_x
    end
    if type(opts.invert_y) == "boolean" then
        cfg.invert_y = opts.invert_y
    end
    if type(opts.skip_same_target_until_near) == "boolean" then
        cfg.skip_same_target_until_near = opts.skip_same_target_until_near
    end

    if type(opts.log_errors) == "boolean" then
        cfg.log_errors = opts.log_errors
    end

    return cfg
end

local function resolve_move_call_mouse_sync_opts(opts)
    local base = nav.move_call_mouse_sync or {}
    local override = opts

    if override == false then
        return {
            enabled = false
        }
    end

    if override == true then
        override = {}
    end

    if type(override) ~= "table" then
        override = {}
    end

    local cfg = {
        enabled = base.enabled == true,
        mode = tostring(base.mode or "direction"),
        radius = as_number(base.radius) or 140,
        swap_axes = base.swap_axes == true,
        invert_x = base.invert_x == true,
        invert_y = base.invert_y == true,
        skip_same_target_until_near = base.skip_same_target_until_near == true,
        near_target_distance = as_number(base.near_target_distance) or 160,
        target_change_distance = as_number(base.target_change_distance) or 12,
        padding_left = as_number(base.padding_left) or 80,
        padding_right = as_number(base.padding_right) or 80,
        padding_top = as_number(base.padding_top) or 80,
        padding_bottom = as_number(base.padding_bottom) or 120,
        log_errors = base.log_errors == true
    }

    if type(override.enabled) == "boolean" then
        cfg.enabled = override.enabled
    end
    if type(override.mode) == "string" and override.mode ~= "" then
        cfg.mode = override.mode
    end
    if type(override.swap_axes) == "boolean" then
        cfg.swap_axes = override.swap_axes
    end
    if type(override.invert_x) == "boolean" then
        cfg.invert_x = override.invert_x
    end
    if type(override.invert_y) == "boolean" then
        cfg.invert_y = override.invert_y
    end
    if type(override.skip_same_target_until_near) == "boolean" then
        cfg.skip_same_target_until_near = override.skip_same_target_until_near
    end
    if type(override.log_errors) == "boolean" then
        cfg.log_errors = override.log_errors
    end

    for _, key in ipairs({
        "radius",
        "near_target_distance",
        "target_change_distance",
        "padding_left",
        "padding_right",
        "padding_top",
        "padding_bottom"
    }) do
        local value = as_number(override[key])
        if value ~= nil then
            cfg[key] = value
        end
    end

    return cfg
end

function nav.project_move_call_mouse_target(target_x, target_y, opts)
    opts = resolve_move_call_mouse_sync_opts(opts)

    local current_x, current_y, _, pos_err = nav.player_pos()
    if current_x == nil or current_y == nil then
        return nil, pos_err or "Unable to read player position."
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

    local dx = target_x - current_x
    local dy = target_y - current_y
    local world_distance = distance_2d(current_x, current_y, target_x, target_y)

    local center_x = client_w * 0.5
    local center_y = client_h * 0.5
    local client_target_x = center_x
    local client_target_y = center_y

    if world_distance >= 1 then
        local screen_dx = dx - dy
        local screen_dy = (dx + dy) * 0.5

        if opts.swap_axes == true then
            screen_dx, screen_dy = screen_dy, screen_dx
        end
        if opts.invert_x == true then
            screen_dx = -screen_dx
        end
        if opts.invert_y == true then
            screen_dy = -screen_dy
        end

        local screen_distance = math.sqrt(screen_dx * screen_dx + screen_dy * screen_dy)

        if screen_distance >= 1 then
            if tostring(opts.mode or "direction") == "target" then
                client_target_x = center_x + screen_dx
                client_target_y = center_y + screen_dy
            else
                local ratio = (opts.radius or 140) / screen_distance
                client_target_x = center_x + screen_dx * ratio
                client_target_y = center_y + screen_dy * ratio
            end
        end
    end

    local min_x = opts.padding_left or 0
    local max_x = client_w - (opts.padding_right or 0)
    local min_y = opts.padding_top or 0
    local max_y = client_h - (opts.padding_bottom or 0)

    if max_x < min_x then
        min_x = 0
        max_x = client_w
    end
    if max_y < min_y then
        min_y = 0
        max_y = client_h
    end

    client_target_x = math.floor(clamp(client_target_x, min_x, max_x))
    client_target_y = math.floor(clamp(client_target_y, min_y, max_y))

    return {
        hwnd = hwnd,
        client_x = client_target_x,
        client_y = client_target_y,
        screen_x = origin_x + client_target_x,
        screen_y = origin_y + client_target_y,
        current_x = current_x,
        current_y = current_y,
        target_x = target_x,
        target_y = target_y,
        world_distance = world_distance,
        mode = tostring(opts.mode or "direction"),
        swap_axes = opts.swap_axes == true,
        invert_x = opts.invert_x == true,
        invert_y = opts.invert_y == true
    }
end

function nav.move_mouse_for_move_call(target_x, target_y, opts)
    local cfg = resolve_move_call_mouse_sync_opts(opts)
    if cfg.enabled ~= true then
        return false, "Move-call mouse sync disabled."
    end

    local point, err = nav.project_move_call_mouse_target(target_x, target_y, cfg)
    if not point then
        return nil, err
    end

    local ok, move_err = human_mouse.move_to(point.screen_x, point.screen_y, {
        hwnd = point.hwnd,
        mouse_mode = "api",
        min_duration_ms = 300,
        max_duration_ms = 2000
    })
    if not ok then
        return nil, move_err or "human mouse move failed."
    end

    return point
end

local function should_sync_mouse_for_move_call(target_x, target_y, opts)
    local cfg = resolve_move_call_mouse_sync_opts(opts)
    if cfg.enabled ~= true then
        return false, nil, "Move-call mouse sync disabled."
    end

    local last = nav.last_move_call_target
    if cfg.skip_same_target_until_near == true and type(last) == "table" then
        local last_x = as_number(last.x)
        local last_y = as_number(last.y)
        if last_x ~= nil and last_y ~= nil then
            local target_gap = distance_2d(last_x, last_y, target_x, target_y)
            if target_gap <= (cfg.target_change_distance or 12) then
                local current_x, current_y = nav.player_pos()
                if current_x == nil or current_y == nil then
                    return false, nil, "Unable to read player position for repeat-target mouse sync."
                end

                local target_distance = distance_2d(current_x, current_y, target_x, target_y)
                if target_distance > (cfg.near_target_distance or 160) then
                    return false, target_distance, "Repeat target is still far away."
                end

                return true, target_distance
            end
        end
    end

    return true
end

function nav.cursor_client_pos(opts)
    opts = opts or {}

    if type(mouse) ~= "table" or type(mouse.position) ~= "function" then
        return nil, "mouse.position is not available."
    end

    if type(wnd) ~= "table" or type(wnd.client_rect) ~= "function" then
        return nil, "wnd.client_rect is not available."
    end

    local hwnd = opts.hwnd
    if not hwnd then
        local hwnd_err
        hwnd, hwnd_err = nav.window_hwnd()
        if not hwnd then
            return nil, hwnd_err
        end
    end

    local client_x, client_y, client_w, client_h = wnd.client_rect(hwnd)
    if type(client_x) ~= "number"
        or type(client_y) ~= "number"
        or type(client_w) ~= "number"
        or type(client_h) ~= "number"
    then
        return nil, "wnd.client_rect failed."
    end

    local screen_x, screen_y = mouse.position()
    if type(screen_x) ~= "number" or type(screen_y) ~= "number" then
        return nil, "mouse.position failed."
    end

    local relative_x = screen_x - client_x
    local relative_y = screen_y - client_y

    if opts.allow_outside ~= true then
        if relative_x < 0 or relative_y < 0 or relative_x > client_w or relative_y > client_h then
            return nil, string.format(
                "Mouse is outside game client. screen=(%.2f, %.2f) client=(%.2f, %.2f)",
                screen_x,
                screen_y,
                relative_x,
                relative_y
            )
        end
    end

    return {
        hwnd = hwnd,
        screen_x = screen_x,
        screen_y = screen_y,
        client_x = relative_x,
        client_y = relative_y,
        client_w = client_w,
        client_h = client_h,
        origin_x = client_x,
        origin_y = client_y
    }
end

function nav.move_mouse_to_client(client_x, client_y, opts)
    opts = opts or {}

    if type(wnd) ~= "table" or type(wnd.client_rect) ~= "function" then
        return false, "wnd.client_rect is not available."
    end

    local hwnd = opts.hwnd
    if not hwnd then
        local hwnd_err
        hwnd, hwnd_err = nav.window_hwnd()
        if not hwnd then
            return false, hwnd_err
        end
    end

    local origin_x, origin_y, client_w, client_h = wnd.client_rect(hwnd)
    if type(origin_x) ~= "number"
        or type(origin_y) ~= "number"
        or type(client_w) ~= "number"
        or type(client_h) ~= "number"
    then
        return false, "wnd.client_rect failed."
    end

    local x = as_number(client_x)
    local y = as_number(client_y)
    if x == nil or y == nil then
        return false, "client mouse target is invalid."
    end

    if opts.clamp ~= false then
        x = clamp(x, 0, client_w)
        y = clamp(y, 0, client_h)
    end

    local screen_x = origin_x + x
    local screen_y = origin_y + y
    local ok, move_err = human_mouse.move_to(screen_x, screen_y, {
        hwnd = hwnd,
        mouse_mode = opts.mouse_mode or "api",
        min_duration_ms = opts.min_duration_ms or 80,
        max_duration_ms = opts.max_duration_ms or 180,
        set_foreground = opts.set_foreground == true
    })
    if not ok then
        return false, move_err or "human mouse move failed."
    end

    local hover_ms = math.max(0, tonumber(opts.hover_ms) or 120)
    if hover_ms > 0 then
        sys.sleep(hover_ms)
    end

    return true, {
        hwnd = hwnd,
        client_x = x,
        client_y = y,
        screen_x = screen_x,
        screen_y = screen_y,
        origin_x = origin_x,
        origin_y = origin_y,
        client_w = client_w,
        client_h = client_h
    }
end

function nav.find_control_at_point(client_x, client_y, opts)
    opts = opts or {}

    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return nil, err
    end

    local controls = collect_controls(snapshot, opts)
    if #controls == 0 then
        return nil, "No UI control candidate found."
    end

    local best = nil

    for _, info in ipairs(controls) do
        local distance = distance_2d(client_x, client_y, info.x, info.y)
        local score = distance + control_priority(info.kind)

        if not best or score < best.score then
            best = {
                kind = info.kind,
                addr = info.addr,
                name = info.name,
                text = info.text,
                fullname = info.fullname,
                x = info.x,
                y = info.y,
                item = info.item,
                distance = distance,
                score = score
            }
        end
    end

    local max_distance = opts.max_distance
    if type(max_distance) == "number" and best.distance > max_distance then
        return nil, string.format(
            "No control near cursor. best=%s distance=%.2f",
            best.fullname ~= "" and best.fullname or best.name ~= "" and best.name or best.kind,
            best.distance
        )
    end

    return best
end

function nav.find_controls_at_point(client_x, client_y, opts)
    opts = opts or {}

    local snapshot = opts.snapshot
    if type(snapshot) ~= "table" then
        local err = nil
        snapshot, err = nav.enum_ui()
        if not snapshot then
            return nil, err
        end
    end

    if opts.include_texts == nil then
        opts.include_texts = true
    end

    local controls = collect_controls(snapshot, opts)
    if #controls == 0 then
        return nil, "No UI control candidate found."
    end

    local matches = {}
    local max_distance = opts.max_distance

    for _, info in ipairs(controls) do
        local distance = distance_2d(client_x, client_y, info.x, info.y)
        if type(max_distance) ~= "number" or distance <= max_distance then
            matches[#matches + 1] = {
                kind = info.kind,
                addr = info.addr,
                name = info.name,
                text = info.text,
                fullname = info.fullname,
                x = info.x,
                y = info.y,
                item = info.item,
                distance = distance,
                score = distance + control_priority(info.kind)
            }
        end
    end

    if #matches == 0 then
        return nil, string.format("No control within %.2f px.", tonumber(max_distance) or 0)
    end

    table.sort(matches, function(a, b)
        if a.score == b.score then
            return (a.addr or 0) < (b.addr or 0)
        end
        return a.score < b.score
    end)

    local limit = opts.limit
    if type(limit) == "number" and limit > 0 and #matches > limit then
        local clipped = {}
        for index = 1, limit do
            clipped[index] = matches[index]
        end
        matches = clipped
    end

    return matches
end

function nav.find_control_at_cursor(opts)
    opts = opts or {}

    local cursor, err = nav.cursor_client_pos(opts)
    if not cursor then
        return nil, err
    end

    local match_opts = {}
    for key, value in pairs(opts) do
        match_opts[key] = value
    end
    if match_opts.include_texts == nil then
        match_opts.include_texts = true
    end

    local match, match_err = nav.find_control_at_point(cursor.client_x, cursor.client_y, match_opts)
    if not match then
        return nil, match_err
    end

    match.cursor_client_x = cursor.client_x
    match.cursor_client_y = cursor.client_y
    match.cursor_screen_x = cursor.screen_x
    match.cursor_screen_y = cursor.screen_y
    match.hwnd = cursor.hwnd

    nav.last_cursor_match = match
    return match
end

function nav.find_controls_at_cursor(opts)
    opts = opts or {}

    local cursor, err = nav.cursor_client_pos(opts)
    if not cursor then
        return nil, err
    end

    local match_opts = {}
    for key, value in pairs(opts) do
        match_opts[key] = value
    end
    if match_opts.include_texts == nil then
        match_opts.include_texts = true
    end

    local matches, match_err = nav.find_controls_at_point(cursor.client_x, cursor.client_y, match_opts)
    if not matches then
        return nil, match_err
    end

    for _, match in ipairs(matches) do
        match.cursor_client_x = cursor.client_x
        match.cursor_client_y = cursor.client_y
        match.cursor_screen_x = cursor.screen_x
        match.cursor_screen_y = cursor.screen_y
        match.hwnd = cursor.hwnd
    end

    nav.last_cursor_match = matches[1]
    nav.last_cursor_matches = matches
    return matches
end

function nav.dump_control_at_cursor(opts)
    opts = opts or {}

    local match, err = nav.find_control_at_cursor(opts)
    if not match then
        return false, err
    end

    local related_controls = nil
    local snapshot = nav.enum_ui()
    if snapshot then
        related_controls = collect_controls(snapshot, {
            include_buttons = true,
            include_texts = true,
            include_images = false
        })
    end

    log.info(string.format(
        "Cursor visible controls | controls=1 cursor=(%.2f, %.2f) screen=(%.2f, %.2f) distance=%.2f",
        tonumber(match.cursor_client_x) or 0,
        tonumber(match.cursor_client_y) or 0,
        tonumber(match.cursor_screen_x) or 0,
        tonumber(match.cursor_screen_y) or 0,
        tonumber(match.distance) or 0
    ))
    log.info(string.format(
        "[Visible][1] %s",
        format_control_dump_fields(match, nil, {
            related_controls = related_controls,
            include_complex_fields = opts.include_complex_fields ~= false
        })
    ))

    if opts.dump_item ~= false then
        local item_dump = format_control_item_dump(match, {
            dump_depth = opts.dump_depth,
            dump_table_limit = opts.dump_table_limit
        })
        if item_dump ~= "" then
            log.info(string.format("[VisibleItem][1] item=%s", item_dump))
        end
    end

    return true, match
end

function nav.dump_controls_at_cursor(opts)
    opts = opts or {}

    local matches, err = nav.find_controls_at_cursor(opts)
    if not matches then
        return false, err
    end

    local first = matches[1]
    log.info(string.format(
        "Cursor visible controls | controls=%d cursor=(%.2f, %.2f) screen=(%.2f, %.2f)",
        #matches,
        tonumber(first.cursor_client_x) or 0,
        tonumber(first.cursor_client_y) or 0,
        tonumber(first.cursor_screen_x) or 0,
        tonumber(first.cursor_screen_y) or 0
    ))

    local related_controls = nil
    local snapshot = nav.enum_ui()
    if snapshot then
        related_controls = collect_controls(snapshot, {
            include_buttons = true,
            include_texts = true,
            include_images = false
        })
    end

    for index, match in ipairs(matches) do
        log.info(string.format(
            "[Visible][%d] %s",
            index,
            format_control_dump_fields(match, nil, {
                related_controls = related_controls,
                include_complex_fields = opts.include_complex_fields ~= false
            })
        ))

        if opts.dump_item ~= false then
            local item_dump = format_control_item_dump(match, {
                dump_depth = opts.dump_depth,
                dump_table_limit = opts.dump_table_limit
            })
            if item_dump ~= "" then
                log.info(string.format("[VisibleItem][%d] item=%s", index, item_dump))
            end
        end
    end

    return true, matches
end

function nav.click_button_by_match(opts)
    local item, err = nav.find_button_by_match(opts)
    if not item then
        return false, err
    end

    local ok, click_err = nav.control_click(item.addr)
    if not ok then
        return false, click_err
    end

    return true, {
        kind = "button",
        addr = item.addr,
        name = item.name,
        text = item.text,
        fullname = item.Fullname,
        x = item.x,
        y = item.y
    }
end

function nav.find_control_by_match(opts)
    opts = opts or {}

    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return nil, err
    end

    local controls = collect_controls(snapshot, opts)
    if #controls == 0 then
        return nil, "Matched control not found."
    end

    local include = opts.include_patterns or {}
    local exclude = opts.exclude_patterns or {}
    local best = nil

    for _, info in ipairs(controls) do
        local text = table.concat({
            tostring(info.name or ""),
            tostring(info.text or ""),
            tostring(info.fullname or "")
        }, " ")

        if (#include == 0 or match_any(text, include)) and not match_any(text, exclude) then
            local score = control_priority(info.kind)
            if not best or score < best.score then
                best = {
                    kind = info.kind,
                    addr = info.addr,
                    name = info.name,
                    text = info.text,
                    fullname = info.fullname,
                    x = info.x,
                    y = info.y,
                    score = score
                }
            end
        end
    end

    if not best then
        return nil, "Matched control not found."
    end

    return best
end

function nav.click_control_by_match(opts)
    opts = opts or {}

    local match, err = nav.find_control_by_match(opts)
    if not match then
        return false, err
    end

    local target = match
    if opts.prefer_button_neighbor ~= false and match.kind ~= "button" then
        local near_button = nav.find_button_near_point(match.x, match.y, {
            include_patterns = opts.neighbor_include_patterns,
            exclude_patterns = opts.neighbor_exclude_patterns,
            max_distance = opts.neighbor_max_distance or 120
        })
        if near_button then
            target = near_button
            target.anchor_kind = match.kind
            target.anchor_addr = match.addr
            target.anchor_name = match.name
            target.anchor_text = match.text
            target.anchor_fullname = match.fullname
        end
    end

    local ok, click_err = nav.control_click(target.addr)
    if not ok then
        return false, click_err
    end

    return true, target
end

function nav.find_controls_by_match(opts)
    opts = opts or {}

    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return nil, err
    end

    local controls = collect_controls(snapshot, opts)
    if #controls == 0 then
        return nil, "Matched control not found."
    end

    local text_rank = {}
    for index, text in ipairs(opts.exact_texts or {}) do
        local normalized = normalize_exact_match_text(text)
        if normalized ~= "" and text_rank[normalized] == nil then
            text_rank[normalized] = index
        end
    end

    table.sort(controls, function(a, b)
        if next(text_rank) ~= nil then
            local ar = text_rank[normalize_exact_match_text(a.text)] or math.huge
            local br = text_rank[normalize_exact_match_text(b.text)] or math.huge
            if ar ~= br then
                return ar < br
            end
        end

        return control_dump_less(a, b)
    end)
    return controls, nil, snapshot
end

local function best_anchor_for_control(match, anchor_points, default_max_distance)
    if type(anchor_points) ~= "table" or #anchor_points == 0 then
        return nil
    end

    local x = as_number(match.x)
    local y = as_number(match.y)
    if x == nil or y == nil then
        return nil
    end

    local best = nil

    for index, anchor in ipairs(anchor_points) do
        local anchor_x = as_number(anchor.x or anchor[1])
        local anchor_y = as_number(anchor.y or anchor[2])
        local max_distance = as_number(anchor.max_distance) or default_max_distance or 140
        if anchor_x ~= nil and anchor_y ~= nil then
            local distance = distance_2d(x, y, anchor_x, anchor_y)
            if distance <= max_distance then
                if not best or distance < best.distance then
                    best = {
                        index = index,
                        x = anchor_x,
                        y = anchor_y,
                        distance = distance,
                        label = tostring(anchor.label or anchor.name or "")
                    }
                end
            end
        end
    end

    return best
end

function nav.click_control_button_by_match(opts)
    opts = opts or {}

    local matches, err = nav.find_controls_by_match(opts)
    if not matches then
        return false, err
    end

    local best = nil

    for _, match in ipairs(matches) do
        local anchor = best_anchor_for_control(match, opts.anchor_points, opts.anchor_max_distance)
        if opts.anchor_points and not anchor then
            goto continue
        end

        if match.kind == "button" and type(match.addr) == "number" then
            best = {
                anchor = match,
                target = {
                    kind = "button",
                    addr = match.addr,
                    name = match.name,
                    text = match.text,
                    fullname = match.fullname,
                    x = match.x,
                    y = match.y,
                    distance = 0
                },
                score = anchor and anchor.distance or 0,
                anchor_ref = anchor
            }
            break
        end

        local near_button = nav.find_button_near_point(match.x, match.y, {
            include_patterns = opts.neighbor_include_patterns,
            exclude_patterns = opts.neighbor_exclude_patterns,
            max_distance = opts.neighbor_max_distance or 160
        })
        if near_button then
            local score = tonumber(near_button.distance) or 0
            if anchor then
                score = score + anchor.distance * 1000
            end
            if not best or score < best.score then
                best = {
                    anchor = match,
                    target = near_button,
                    score = score,
                    anchor_ref = anchor
                }
            end
        end

        ::continue::
    end

    if not best then
        return false, "Matched control found but nearby button not found."
    end

    local ok, click_err = nav.control_click(best.target.addr)
    if not ok then
        return false, click_err
    end

    best.target.anchor_kind = best.anchor.kind
    best.target.anchor_addr = best.anchor.addr
    best.target.anchor_name = best.anchor.name
    best.target.anchor_text = best.anchor.text
    best.target.anchor_fullname = best.anchor.fullname
    best.target.anchor_x = best.anchor.x
    best.target.anchor_y = best.anchor.y
    if best.anchor_ref then
        best.target.anchor_ref_index = best.anchor_ref.index
        best.target.anchor_ref_x = best.anchor_ref.x
        best.target.anchor_ref_y = best.anchor_ref.y
        best.target.anchor_ref_distance = best.anchor_ref.distance
        best.target.anchor_ref_label = best.anchor_ref.label
    end

    return true, best.target
end

function nav.dump_controls_by_match(opts)
    opts = opts or {}

    local matches, err, snapshot = nav.find_controls_by_match(opts)
    if not matches then
        return false, err
    end

    local header = opts.header or "Matched controls"
    log.info(string.format(
        "%s | controls=%d buttons=%d texts=%d images=%d",
        header,
        #matches,
        #(snapshot.buttons or {}),
        #(snapshot.texts or {}),
        #(snapshot.images or {})
    ))

    local related_controls = collect_controls(snapshot, {
        include_buttons = true,
        include_texts = true,
        include_images = true
    })
    local related_opts = opts.related_opts or {
        limit = opts.related_limit or 4,
        max_distance = opts.related_max_distance or 260,
        max_dx = opts.related_max_dx or 360,
        max_dy = opts.related_max_dy or 220
    }

    for index, info in ipairs(matches) do
        log.info(string.format(
            "[Match][%d] %s",
            index,
            format_control_dump_fields(info, nil, {
                related_controls = related_controls,
                related_opts = related_opts,
                include_complex_fields = opts.include_complex_fields ~= false
            })
        ))

        if opts.dump_item ~= false then
            local item_dump = format_control_item_dump(info, {
                dump_depth = opts.dump_depth,
                dump_table_limit = opts.dump_table_limit
            })
            if item_dump ~= "" then
                log.info(string.format("[MatchItem][%d] item=%s", index, item_dump))
            end
        end
    end

    return true, matches
end

function nav.click_last_cursor_control(opts)
    opts = opts or {}

    local match = nav.last_cursor_match
    if type(match) ~= "table" or type(match.addr) ~= "number" then
        return false, "No captured cursor control. Press F6 first."
    end

    local target = match
    if opts.prefer_button_neighbor ~= false and match.kind ~= "button" then
        local near_button = nav.find_button_near_point(match.x, match.y, {
            include_patterns = opts.neighbor_include_patterns,
            exclude_patterns = opts.neighbor_exclude_patterns,
            max_distance = opts.neighbor_max_distance or 120
        })
        if near_button then
            target = near_button
            target.anchor_kind = match.kind
            target.anchor_addr = match.addr
            target.anchor_name = match.name
            target.anchor_text = match.text
            target.anchor_fullname = match.fullname
        end
    end

    local ok, click_err = nav.control_click(target.addr)
    if not ok then
        return false, click_err
    end

    return true, target
end

function nav.click_image_button_at_cursor(opts)
    opts = opts or {}

    local match, err = nav.find_control_at_cursor({
        hwnd = opts.hwnd,
        allow_outside = opts.allow_outside,
        include_buttons = false,
        include_images = true,
        include_texts = false,
        max_distance = opts.max_distance or 120
    })
    if not match then
        return false, err
    end

    if match.kind ~= "image" then
        return false, "Cursor target is not an image."
    end

    local target, target_err = nav.find_button_near_point(match.x, match.y, {
        include_patterns = opts.neighbor_include_patterns,
        exclude_patterns = opts.neighbor_exclude_patterns,
        max_distance = opts.neighbor_max_distance or 160
    })
    if not target then
        return false, target_err or "Nearby button not found."
    end

    local ok, click_err = nav.control_click(target.addr)
    if not ok then
        return false, click_err
    end

    target.anchor_kind = match.kind
    target.anchor_addr = match.addr
    target.anchor_name = match.name
    target.anchor_text = match.text
    target.anchor_fullname = match.fullname
    target.anchor_x = match.x
    target.anchor_y = match.y
    target.cursor_client_x = match.cursor_client_x
    target.cursor_client_y = match.cursor_client_y
    target.cursor_screen_x = match.cursor_screen_x
    target.cursor_screen_y = match.cursor_screen_y
    target.hwnd = match.hwnd

    return true, target
end

function nav.dump_bottom_left_candidates(opts)
    local candidates, err = nav.find_bottom_left_candidates(opts)
    if not candidates then
        return false, err
    end

    log.info(string.format("Bottom-left candidates: %d", #candidates))
    for index, candidate in ipairs(candidates) do
        local item = candidate.item
        log.info(string.format(
            "[%s][%d] %s",
            candidate.kind,
            index,
            format_control_dump_fields(control_dump_info(candidate.kind, item))
        ))
    end

    return true
end

function nav.find_bottom_left_button(opts)
    opts = opts or {}

    local candidates, err = nav.find_bottom_left_candidates(opts)
    if not candidates then
        return nil, err
    end

    if #candidates == 0 then
        return nil, "No bottom-left control candidate found."
    end

    return candidates[1].item, candidates[1].kind
end

function nav.click_bottom_left_button(opts)
    local item, kind_or_err = nav.find_bottom_left_button(opts)
    if not item then
        return false, kind_or_err
    end

    local ok, click_err = nav.control_click(item.addr)
    if not ok then
        return false, click_err
    end

    return true, {
        kind = kind_or_err,
        addr = item.addr,
        name = item.name,
        text = item.text,
        fullname = item.Fullname,
        x = item.x,
        y = item.y
    }
end

local function item_label(item, keys)
    for _, key in ipairs(keys) do
        local value = item[key]
        if value ~= nil and tostring(value) ~= "" then
            return tostring(value)
        end
    end
    return ""
end

local function is_scalar_dump_value(value)
    local value_type = type(value)
    return value_type ~= "table"
        and value_type ~= "function"
        and value_type ~= "userdata"
        and value_type ~= "thread"
end

local function escape_dump_text(value)
    local text = tostring(value or "")
    text = text:gsub("\\", "\\\\")
    text = text:gsub("\r", "\\r")
    text = text:gsub("\n", "\\n")
    text = text:gsub("\t", "\\t")
    text = text:gsub("\"", "\\\"")
    return "\"" .. text .. "\""
end

local function format_dump_value(value)
    if value == nil then
        return "nil"
    end

    local value_type = type(value)
    if value_type == "number" or value_type == "boolean" then
        return tostring(value)
    end

    return escape_dump_text(value)
end

local function append_dump_field(parts, key, value, force)
    if not force then
        if value == nil then
            return
        end
        if type(value) == "string" and value == "" then
            return
        end
    end

    parts[#parts + 1] = tostring(key) .. "=" .. format_dump_value(value)
end

local function collect_item_extra_fields(item, skip_keys)
    if type(item) ~= "table" then
        return ""
    end

    local keys = {}
    for key, value in pairs(item) do
        if type(key) == "string"
            and not (skip_keys and skip_keys[key])
            and is_scalar_dump_value(value)
        then
            keys[#keys + 1] = key
        end
    end

    table.sort(keys)
    if #keys == 0 then
        return ""
    end

    local parts = {}
    for _, key in ipairs(keys) do
        append_dump_field(parts, key, item[key], false)
    end

    return table.concat(parts, " ")
end

local function shallow_table_summary(tbl, max_items)
    if type(tbl) ~= "table" then
        return ""
    end

    local limit = max_items or 12
    local keys = {}
    for key, _ in pairs(tbl) do
        if type(key) == "string" or type(key) == "number" then
            keys[#keys + 1] = key
        end
    end

    table.sort(keys, function(a, b)
        return tostring(a) < tostring(b)
    end)

    local parts = {}
    local count = 0
    local complex_count = 0

    for _, key in ipairs(keys) do
        local value = tbl[key]
        if is_scalar_dump_value(value) then
            append_dump_field(parts, key, value, false)
            count = count + 1
            if count >= limit then
                break
            end
        else
            complex_count = complex_count + 1
        end
    end

    if complex_count > 0 then
        parts[#parts + 1] = "_complex=" .. tostring(complex_count)
    end

    if #parts == 0 then
        return "{}"
    end

    return "{" .. table.concat(parts, " ") .. "}"
end

local function dump_table_keys(tbl)
    local keys = {}
    for key, _ in pairs(tbl or {}) do
        if type(key) == "string" or type(key) == "number" then
            keys[#keys + 1] = key
        end
    end

    table.sort(keys, function(a, b)
        local at = type(a)
        local bt = type(b)
        if at == bt then
            return tostring(a) < tostring(b)
        end
        return at == "number"
    end)

    return keys
end

local function summarize_deep_dump_value(value, depth, max_items, seen)
    if is_scalar_dump_value(value) then
        return format_dump_value(value)
    end

    local value_type = type(value)
    if value_type ~= "table" then
        return "<" .. value_type .. ">"
    end

    if depth <= 0 then
        return "<table>"
    end

    seen = seen or {}
    if seen[value] then
        return "<cycle>"
    end
    seen[value] = true

    local keys = dump_table_keys(value)
    if #keys == 0 then
        seen[value] = nil
        return "{}"
    end

    local limit = max_items or 16
    local parts = {}
    local count = 0

    for _, key in ipairs(keys) do
        count = count + 1
        if count > limit then
            break
        end

        parts[#parts + 1] = tostring(key) .. "=" .. summarize_deep_dump_value(
            value[key],
            depth - 1,
            max_items,
            seen
        )
    end

    if #keys > limit then
        parts[#parts + 1] = "_more=" .. tostring(#keys - limit)
    end

    seen[value] = nil
    return "{" .. table.concat(parts, " ") .. "}"
end

format_control_item_dump = function(info, opts)
    if type(info) ~= "table" or type(info.item) ~= "table" then
        return ""
    end

    opts = opts or {}
    return summarize_deep_dump_value(
        info.item,
        opts.dump_depth or 2,
        opts.dump_table_limit or 16,
        {}
    )
end

local function collect_item_complex_fields(item, skip_keys)
    if type(item) ~= "table" then
        return ""
    end

    local keys = {}
    for key, value in pairs(item) do
        if type(key) == "string"
            and not (skip_keys and skip_keys[key])
            and not is_scalar_dump_value(value)
        then
            keys[#keys + 1] = key
        end
    end

    table.sort(keys)
    if #keys == 0 then
        return ""
    end

    local parts = {}
    for _, key in ipairs(keys) do
        local value = item[key]
        local value_type = type(value)
        if value_type == "table" then
            parts[#parts + 1] = tostring(key) .. "=" .. shallow_table_summary(value)
        else
            parts[#parts + 1] = tostring(key) .. "=<" .. value_type .. ">"
        end
    end

    return table.concat(parts, " ")
end

local function has_control_label(info)
    return tostring(info.text or "") ~= ""
        or tostring(info.name or "") ~= ""
        or tostring(info.fullname or "") ~= ""
end

local function related_control_penalty(kind)
    if kind == "text" then
        return 0
    end
    if kind == "button" then
        return 12
    end
    return 30
end

local function find_related_controls_for_dump(target, controls, opts)
    opts = opts or {}

    local target_x = as_number(target.x)
    local target_y = as_number(target.y)
    if target_x == nil or target_y == nil then
        return {}
    end

    local max_distance = opts.max_distance or 180
    local max_dx = opts.max_dx or 240
    local max_dy = opts.max_dy or 120
    local limit = opts.limit or 2
    local matches = {}

    for _, candidate in ipairs(controls or {}) do
        if not (
            candidate.kind == target.kind
            and candidate.addr == target.addr
            and candidate.x == target.x
            and candidate.y == target.y
        ) then
            local x = as_number(candidate.x)
            local y = as_number(candidate.y)
            if x ~= nil and y ~= nil and has_control_label(candidate) then
                local dx = x - target_x
                local dy = y - target_y
                local abs_dx = math.abs(dx)
                local abs_dy = math.abs(dy)
                local distance = distance_2d(target_x, target_y, x, y)

                if distance <= max_distance or (abs_dx <= max_dx and abs_dy <= max_dy) then
                    local score = distance + related_control_penalty(candidate.kind)

                    if candidate.kind == "text" then
                        score = score - 20
                    end
                    if tostring(candidate.text or "") ~= "" then
                        score = score - 40
                    end
                    if tostring(candidate.name or "") ~= "" then
                        score = score - 20
                    end
                    if candidate.kind == target.kind then
                        score = score + 25
                    end

                    matches[#matches + 1] = {
                        info = candidate,
                        dx = dx,
                        dy = dy,
                        distance = distance,
                        score = score
                    }
                end
            end
        end
    end

    table.sort(matches, function(a, b)
        if a.score == b.score then
            return a.distance < b.distance
        end
        return a.score < b.score
    end)

    if #matches > limit then
        local clipped = {}
        for index = 1, limit do
            clipped[index] = matches[index]
        end
        matches = clipped
    end

    return matches
end

format_control_dump_fields = function(info, extra_fields, opts)
    opts = opts or {}
    local parts = {}
    append_dump_field(parts, "kind", info.kind, true)
    append_dump_field(parts, "addr", info.addr, true)
    append_dump_field(parts, "text", info.text, true)
    append_dump_field(parts, "name", info.name, true)
    append_dump_field(parts, "fullname", info.fullname, true)
    append_dump_field(parts, "x", info.x, true)
    append_dump_field(parts, "y", info.y, true)

    for _, field in ipairs(extra_fields or {}) do
        local key = field[1]
        local value = field[2]
        local force = field[3]
        append_dump_field(parts, key, value, force == true)
    end

    local raw = collect_item_extra_fields(info.item, {
        addr = true,
        name = true,
        text = true,
        x = true,
        y = true,
        Fullname = true,
        fullname = true
    })
    if raw ~= "" then
        parts[#parts + 1] = "raw={" .. raw .. "}"
    end

    if opts.related_controls then
        local related = find_related_controls_for_dump(info, opts.related_controls, opts.related_opts)
        for index, match in ipairs(related) do
            local prefix = "rel" .. tostring(index)
            append_dump_field(parts, prefix .. "_kind", match.info.kind, true)
            append_dump_field(parts, prefix .. "_text", match.info.text, true)
            append_dump_field(parts, prefix .. "_name", match.info.name, true)
            append_dump_field(parts, prefix .. "_fullname", match.info.fullname, true)
            append_dump_field(parts, prefix .. "_x", match.info.x, true)
            append_dump_field(parts, prefix .. "_y", match.info.y, true)
            append_dump_field(parts, prefix .. "_dx", string.format("%.2f", match.dx), true)
            append_dump_field(parts, prefix .. "_dy", string.format("%.2f", match.dy), true)
            append_dump_field(parts, prefix .. "_dist", string.format("%.2f", match.distance), true)
        end
    end

    if opts.include_complex_fields == true then
        local complex = collect_item_complex_fields(info.item, {
            addr = true,
            name = true,
            text = true,
            x = true,
            y = true,
            Fullname = true,
            fullname = true
        })
        if complex ~= "" then
            parts[#parts + 1] = "complex={" .. complex .. "}"
        end
    end

    return table.concat(parts, " ")
end

control_dump_info = function(kind, item)
    local info = make_control_info(kind, item)
    info.item = item
    return info
end

function nav.ui_signature(snapshot)
    if type(snapshot) ~= "table" then
        return ""
    end

    local parts = {}
    local groups = {
        { name = "B", items = snapshot.buttons or {}, keys = { "name", "text", "addr", "x", "y" } },
        { name = "T", items = snapshot.texts or {}, keys = { "name", "text", "addr", "x", "y" } },
        { name = "I", items = snapshot.images or {}, keys = { "Fullname", "name", "addr", "IsAsyncLoad", "x", "y" } }
    }

    for _, group in ipairs(groups) do
        table.insert(parts, group.name .. ":" .. tostring(#group.items))
        for _, item in ipairs(group.items) do
            local row = {}
            for _, key in ipairs(group.keys) do
                row[#row + 1] = tostring(item[key] or "")
            end
            table.insert(parts, table.concat(row, "|"))
        end
    end

    return table.concat(parts, "\n")
end

function nav.dump_ui(snapshot, opts)
    opts = opts or {}
    snapshot = snapshot or nav.enum_ui()
    if not snapshot then
        return false, "UI enumeration failed."
    end

    local header = opts.header or "UI Snapshot"
    log.info(string.format(
        "%s | buttons=%d texts=%d images=%d",
        header,
        #(snapshot.buttons or {}),
        #(snapshot.texts or {}),
        #(snapshot.images or {})
    ))

    local related_controls = collect_controls(snapshot, {
        include_buttons = true,
        include_texts = true,
        include_images = false
    })

    for index, item in ipairs(snapshot.buttons or {}) do
        log.info(string.format(
            "[Button][%d] %s",
            index,
            format_control_dump_fields(control_dump_info("button", item), nil, {
                related_controls = related_controls
            })
        ))
    end

    for index, item in ipairs(snapshot.texts or {}) do
        log.info(string.format(
            "[Text][%d] %s",
            index,
            format_control_dump_fields(control_dump_info("text", item), nil, {
                related_controls = related_controls
            })
        ))
    end

    for index, item in ipairs(snapshot.images or {}) do
        log.info(string.format(
            "[Image][%d] %s",
            index,
            format_control_dump_fields(control_dump_info("image", item), nil, {
                related_controls = related_controls
            })
        ))
    end

    return true
end

function nav.dump_visible_controls(opts)
    opts = opts or {}

    local controls, snapshot_or_err = nav.list_visible_controls(opts)
    if not controls then
        return false, snapshot_or_err
    end

    local snapshot = snapshot_or_err
    local header = opts.header or "Visible controls"
    log.info(string.format(
        "%s | controls=%d buttons=%d texts=%d images=%d",
        header,
        #controls,
        #(snapshot.buttons or {}),
        #(snapshot.texts or {}),
        #(snapshot.images or {})
    ))

    local related_controls = collect_controls(snapshot, {
        include_buttons = true,
        include_texts = true,
        include_images = false
    })

    for index, info in ipairs(controls) do
        log.info(string.format(
            "[Visible][%d] %s",
            index,
            format_control_dump_fields(info, nil, {
                related_controls = related_controls
            })
        ))
    end

    return true, controls
end

function nav.get_current_selected_button()
    local ok, err = ensure_api()
    if not ok then
        return nil, err
    end

    if type(nav.game_api.GetCurrentSelected) ~= "function" then
        return nil, "GetCurrentSelected is not available."
    end

    local item, item_err = quiet_call(nav.game_api.GetCurrentSelected)
    if not item then
        return nil, item_err or "Current selected button not found."
    end
    if type(item) ~= "table" then
        return nil, "GetCurrentSelected returned invalid button data."
    end

    return item
end

local function is_current_selected_api_empty_error(err)
    local text = lower_text(err)
    return text:find("name_id", 1, true) ~= nil
        or text:find("bitwise operation", 1, true) ~= nil
        or text:find("currentselected", 1, true) ~= nil
end

function nav.dump_current_selected_button(opts)
    opts = opts or {}

    local item, err = nav.get_current_selected_button()
    local info = nil
    local extra_fields = {}

    if item then
        info = control_dump_info("button", item)
        extra_fields[#extra_fields + 1] = { "source", "GetCurrentSelected", true }
    elseif opts.fallback_to_cursor ~= false and is_current_selected_api_empty_error(err) then
        local match, cursor_err = nav.find_control_at_cursor({
            include_buttons = true,
            include_images = false,
            include_texts = false,
            max_distance = opts.max_distance or 180
        })
        if not match then
            return false, string.format(
                "GetCurrentSelected failed: %s; cursor fallback failed: %s",
                tostring(err),
                tostring(cursor_err)
            )
        end

        info = match
        extra_fields[#extra_fields + 1] = { "source", "cursor_fallback", true }
        extra_fields[#extra_fields + 1] = { "api_error", tostring(err), true }
        extra_fields[#extra_fields + 1] = { "cursor_x", match.cursor_client_x, true }
        extra_fields[#extra_fields + 1] = { "cursor_y", match.cursor_client_y, true }
        extra_fields[#extra_fields + 1] = { "screen_x", match.cursor_screen_x, true }
        extra_fields[#extra_fields + 1] = { "screen_y", match.cursor_screen_y, true }
        extra_fields[#extra_fields + 1] = { "distance", string.format("%.2f", tonumber(match.distance) or 0), true }
    else
        return false, err
    end

    local snapshot = nil
    local related_controls = nil
    local snapshot_ok, snapshot_err = nav.enum_ui()
    if snapshot_ok then
        snapshot = snapshot_ok
        related_controls = collect_controls(snapshot, {
            include_buttons = true,
            include_texts = true,
            include_images = false
        })
    elseif opts.log_snapshot_error == true then
        log.warn("Current selected button snapshot failed: " .. tostring(snapshot_err))
    end

    if related_controls then
        local related = find_related_controls_for_dump(info, related_controls, opts.related_opts)
        info.related_controls = related
        for index, match in ipairs(related) do
            local prefix = "rel" .. tostring(index)
            info[prefix .. "_kind"] = match.info.kind
            info[prefix .. "_text"] = match.info.text
            info[prefix .. "_name"] = match.info.name
            info[prefix .. "_fullname"] = match.info.fullname
            info[prefix .. "_x"] = match.info.x
            info[prefix .. "_y"] = match.info.y
            info[prefix .. "_dx"] = match.dx
            info[prefix .. "_dy"] = match.dy
            info[prefix .. "_dist"] = match.distance
        end
    end

    local header = opts.header or "Current selected button"
    if snapshot then
        log.info(string.format(
            "%s | buttons=%d texts=%d images=%d",
            header,
            #(snapshot.buttons or {}),
            #(snapshot.texts or {}),
            #(snapshot.images or {})
        ))
    else
        log.info(header)
    end

    log.info(string.format(
        "[Selected][1] %s",
        format_control_dump_fields(info, extra_fields, {
            related_controls = related_controls,
            include_complex_fields = opts.include_complex_fields ~= false
        })
    ))

    if opts.dump_item ~= false then
        local item_dump = format_control_item_dump(info, {
            dump_depth = opts.dump_depth,
            dump_table_limit = opts.dump_table_limit
        })
        if item_dump ~= "" then
            log.info(string.format("[SelectedItem][1] item=%s", item_dump))
        end
    end

    return true, info
end

function nav.dump_raw_current_selected_button(opts)
    opts = opts or {}

    local item, err = nav.get_current_selected_button()
    if not item then
        return false, err
    end

    local header = opts.header or "GetCurrentSelected raw dump"
    log.info(header)

    local scalar_fields = collect_item_extra_fields(item)
    if scalar_fields ~= "" then
        log.info("[SelectedRawFields][1] " .. scalar_fields)
    end

    local raw_dump = summarize_deep_dump_value(
        item,
        opts.dump_depth or 4,
        opts.dump_table_limit or 48,
        {}
    )
    if raw_dump ~= "" then
        log.info(string.format("[SelectedRawItem][1] item=%s", raw_dump))
    end

    return true, item
end

function nav.player_pos()
    local info, err = nav.player_info()
    if not info then
        return nil, nil, nil, err
    end

    local x, y, z = extract_position(info)
    if x == nil or y == nil then
        return nil, nil, nil, "Unable to parse player coordinates."
    end

    return x, y, z
end

function nav.move_call(target_x, target_y, opts)
    opts = opts or {}

    local ok, err = ensure_api()
    if not ok then
        return false, err
    end

    local move_ok, move_err = quiet_call(nav.game_api.MoveTo, target_x, target_y)
    if move_ok == nil or move_ok == false then
        return false, move_err or "MoveTo call failed."
    end

    local mouse_sync_opts = opts.move_call_mouse_sync
    local mouse_sync_cfg = resolve_move_call_mouse_sync_opts(mouse_sync_opts)
    local mouse_result = nil

    if mouse_sync_cfg.enabled == true then
        local should_sync, distance_or_nil, sync_reason = should_sync_mouse_for_move_call(
            target_x,
            target_y,
            mouse_sync_opts
        )
        if should_sync then
            local mouse_err
            mouse_result, mouse_err = nav.move_mouse_for_move_call(target_x, target_y, mouse_sync_opts)
            if not mouse_result and mouse_sync_cfg.log_errors == true then
                log.warn("MoveTo mouse sync failed: " .. tostring(mouse_err))
            end
        elseif mouse_sync_cfg.log_errors == true and sync_reason then
            local detail = sync_reason
            if type(distance_or_nil) == "number" then
                detail = string.format("%s distance=%.2f", detail, distance_or_nil)
            end
            log.info("MoveTo mouse sync skipped: " .. detail)
        end
    end

    nav.last_move_call_target = {
        x = target_x,
        y = target_y,
        t = type(sys) == "table" and type(sys.time) == "function" and sys.time() or 0
    }

    return true, mouse_result
end

function nav.wait_until_arrive(target_x, target_y, opts)
    opts = opts or {}

    local timeout_ms = opts.timeout_ms or 20000
    local interval_ms = opts.interval_ms or 100
    local tolerance = opts.tolerance or 120
    local repath_interval_ms = opts.repath_interval_ms or 1500

    local deadline = sys.time() + timeout_ms
    local next_repath_at = 0

    while sys.time() < deadline do
        local now = sys.time()
        local x, y, _, err = nav.player_pos()
        if x ~= nil and y ~= nil then
            if distance_2d(x, y, target_x, target_y) <= tolerance then
                return true
            end
        elseif err then
            log.warn("Read player position failed: " .. tostring(err))
        end

        if repath_interval_ms > 0 and now >= next_repath_at then
            local move_ok, move_err = nav.move_call(target_x, target_y)
            if not move_ok then
                return false, move_err
            end
            next_repath_at = now + repath_interval_ms
        end

        sys.sleep(interval_ms)
    end

    return false, "Move timeout."
end

function nav.click_move_to(target_x, target_y, opts)
    opts = opts or {}

    local ok, err = nav.move_call(target_x, target_y)
    if not ok then
        return false, err
    end

    if opts.wait == false then
        return true
    end

    return nav.wait_until_arrive(target_x, target_y, opts)
end

function nav.walk_to(target_x, target_y, opts)
    opts = opts or {}

    local ok, err = nav.ensure_initialized(
        opts.process_name or opts.pid or opts.target,
        opts.mode
    )
    if not ok then
        return false, err
    end

    local move_ok, move_err = nav.move_call(target_x, target_y)
    if not move_ok then
        return false, move_err
    end

    if opts.wait == false then
        return true
    end

    return nav.wait_until_arrive(target_x, target_y, opts)
end

nav.walk_by_call = nav.walk_to

function nav.walk_path(points, opts)
    opts = opts or {}

    if type(points) ~= "table" or #points == 0 then
        return false, "Route point list is empty."
    end

    local ok, err = nav.ensure_initialized(
        opts.process_name or opts.pid or opts.target,
        opts.mode
    )
    if not ok then
        return false, err
    end

    for index, point in ipairs(points) do
        local x = as_number(point.x or point[1])
        local y = as_number(point.y or point[2])
        if x == nil or y == nil then
            return false, string.format("Route point %d is invalid.", index)
        end

        local step_ok, step_err = nav.walk_to(x, y, opts)
        if not step_ok then
            return false, string.format("Route point %d failed: %s", index, tostring(step_err))
        end
    end

    return true
end

function nav.click_window_to_move(hwnd, client_x, client_y, opts)
    opts = opts or {}

    local ok = mouse.post_click(hwnd, client_x, client_y, opts.button or "left", opts.delay or 50)
    if not ok then
        return false, "mouse.post_click failed."
    end

    if opts.wait == false then
        return true
    end

    if opts.target_x == nil or opts.target_y == nil then
        return true
    end

    return nav.wait_until_arrive(opts.target_x, opts.target_y, opts)
end

return nav
